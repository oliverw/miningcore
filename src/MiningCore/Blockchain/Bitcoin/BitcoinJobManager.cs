/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using MiningCore.Blockchain.Bitcoin.Configuration;
using MiningCore.Blockchain.Bitcoin.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.Contracts;
using MiningCore.Crypto;
using MiningCore.Crypto.Hashing.Algorithms;
using MiningCore.Crypto.Hashing.Special;
using MiningCore.DaemonInterface;
using MiningCore.Extensions;
using MiningCore.Notifications;
using MiningCore.Stratum;
using MiningCore.Time;
using MiningCore.Util;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace MiningCore.Blockchain.Bitcoin
{
    public class BitcoinJobManager<TJob, TBlockTemplate> : JobManagerBase<TJob>
        where TBlockTemplate : BlockTemplate
        where TJob : BitcoinJob<TBlockTemplate>, new()
    {
        public BitcoinJobManager(
            IComponentContext ctx,
            NotificationService notificationService,
            IMasterClock clock,
            IExtraNonceProvider extraNonceProvider) :
            base(ctx)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(notificationService, nameof(notificationService));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(extraNonceProvider, nameof(extraNonceProvider));

            this.notificationService = notificationService;
            this.clock = clock;
            this.extraNonceProvider = extraNonceProvider;
        }

        protected readonly NotificationService notificationService;
        protected readonly IMasterClock clock;
        protected DaemonClient daemon;
        protected readonly IExtraNonceProvider extraNonceProvider;
        protected const int ExtranonceBytes = 4;
        protected readonly IHashAlgorithm sha256d = new Sha256D();
        protected readonly IHashAlgorithm sha256dReverse = new DigestReverser(new Sha256D());
        protected int maxActiveJobs = 4;
        protected bool hasLegacyDaemon;
        protected BitcoinPoolConfigExtra extraPoolConfig;
        protected BitcoinPoolPaymentProcessingConfigExtra extraPoolPaymentProcessingConfig;
        protected readonly IHashAlgorithm sha256s = new Sha256S();
        protected readonly List<TJob> validJobs = new List<TJob>();
        protected IHashAlgorithm blockHasher;
        protected IHashAlgorithm coinbaseHasher;
        protected bool hasSubmitBlockMethod;
        protected IHashAlgorithm headerHasher;
        protected bool isPoS;
        protected TimeSpan jobRebroadcastTimeout;
        protected BitcoinNetworkType networkType;
        protected IDestination poolAddressDestination;

        protected object[] getBlockTemplateParams =
        {
            new
            {
                capabilities = new[] { "coinbasetxn", "workid", "coinbase/append" },
                rules = new[] { "segwit" }
            }
        };

        protected virtual void SetupJobUpdates()
        {
	        if (poolConfig.EnableInternalStratum == false)
		        return;

            jobRebroadcastTimeout = TimeSpan.FromSeconds(Math.Max(1, poolConfig.JobRebroadcastTimeout));
            var blockSubmission = blockSubmissionSubject.Synchronize();
            var pollTimerRestart = blockSubmissionSubject.Synchronize();

            var triggers = new List<IObservable<(bool Force, string Via)>>
            {
                blockSubmission.Select(x=> (false, "Block-submission"))
            };

            // collect ports
            var zmq = poolConfig.Daemons
                .Where(x => !string.IsNullOrEmpty(x.Extra.SafeExtensionDataAs<BitcoinDaemonEndpointConfigExtra>()?.ZmqBlockNotifySocket))
                .ToDictionary(x => x, x =>
                {
                    var extra = x.Extra.SafeExtensionDataAs<BitcoinDaemonEndpointConfigExtra>();
                    var topic = !string.IsNullOrEmpty(extra.ZmqBlockNotifyTopic?.Trim()) ?
                        extra.ZmqBlockNotifyTopic.Trim() : BitcoinConstants.ZmqPublisherTopicBlockHash;

                    return (Socket: extra.ZmqBlockNotifySocket, Topic: topic);
                });

            if (zmq.Count > 0)
            {
                logger.Info(() => $"[{LogCat}] Subscribing to ZMQ push-updates from {string.Join(", ", zmq.Values)}");

                var blockNotify = daemon.ZmqSubscribe(zmq, 2)
                    .Select(frames =>
                    {
                        // We just take the second frame's raw data and turn it into a hex string.
                        // If that string changes, we got an update (DistinctUntilChanged)
                        var result = frames[1].ToHexString();
                        frames.Dispose();
                        return result;
                    })
                    .DistinctUntilChanged()
                    .Select(_ => (false, "ZMQ pub/sub"))
                    .Publish()
                    .RefCount();

                pollTimerRestart = Observable.Merge(
                        blockSubmission,
                        blockNotify.Select(_ => Unit.Default))
                    .Publish()
                    .RefCount();

                triggers.Add(blockNotify);
            }

            if (poolConfig.BlockRefreshInterval > 0)
            {
                // periodically update block-template
                triggers.Add(Observable.Timer(TimeSpan.FromMilliseconds(poolConfig.BlockRefreshInterval))
                    .TakeUntil(pollTimerRestart)
                    .Select(_ => (false, "RPC polling"))
                    .Repeat());
            }

            else
            {
                // get initial blocktemplate
                triggers.Add(Observable.Interval(TimeSpan.FromMilliseconds(1000))
                    .Select(_ => (false, "Initial template"))
                    .TakeWhile(_ => !hasInitialBlockTemplate));
            }

            // periodically update transactions for current template
            triggers.Add(Observable.Timer(jobRebroadcastTimeout)
                .TakeUntil(pollTimerRestart)
                .Select(_ => (true, "Job-Refresh"))
                .Repeat());

            Jobs = Observable.Merge(triggers)
                .Select(x => Observable.FromAsync(() => UpdateJob(x.Force, x.Via)))
                .Concat()
                .Where(x=> x.IsNew || x.Force)
                .Do(x =>
                {
                    if(x.IsNew)
                        hasInitialBlockTemplate = true;
                })
                .Select(x=> GetJobParamsForStratum(x.IsNew))
                .Publish()
                .RefCount();
        }

        protected virtual async Task<DaemonResponse<TBlockTemplate>> GetBlockTemplateAsync()
        {
            logger.LogInvoke(LogCat);

            var result = await daemon.ExecuteCmdAnyAsync<TBlockTemplate>(
                BitcoinCommands.GetBlockTemplate, getBlockTemplateParams);

            return result;
        }

        protected virtual async Task ShowDaemonSyncProgressAsync()
        {
            if (hasLegacyDaemon)
            {
                await ShowDaemonSyncProgressLegacyAsync();
                return;
            }

            var infos = await daemon.ExecuteCmdAllAsync<BlockchainInfo>(BitcoinCommands.GetBlockchainInfo);

            if (infos.Length > 0)
            {
                var blockCount = infos
                    .Max(x => x.Response?.Blocks);

                if (blockCount.HasValue)
                {
                    // get list of peers and their highest block height to compare to ours
                    var peerInfo = await daemon.ExecuteCmdAnyAsync<PeerInfo[]>(BitcoinCommands.GetPeerInfo);
                    var peers = peerInfo.Response;

                    if (peers != null && peers.Length > 0)
                    {
                        var totalBlocks = peers.Max(x => x.StartingHeight);
                        var percent = totalBlocks > 0 ? (double) blockCount / totalBlocks * 100 : 0;
                        logger.Info(() => $"[{LogCat}] Daemons have downloaded {percent:0.00}% of blockchain from {peers.Length} peers");
                    }
                }
            }
        }

        private async Task UpdateNetworkStatsAsync()
        {
            logger.LogInvoke(LogCat);

            var results = await daemon.ExecuteBatchAnyAsync(
                new DaemonCmd(BitcoinCommands.GetBlockchainInfo),
                new DaemonCmd(BitcoinCommands.GetMiningInfo),
                new DaemonCmd(BitcoinCommands.GetNetworkInfo)
            );

            if (results.Any(x => x.Error != null))
            {
                var errors = results.Where(x => x.Error != null).ToArray();

                if (errors.Any())
                    logger.Warn(() => $"[{LogCat}] Error(s) refreshing network stats: {string.Join(", ", errors.Select(y => y.Error.Message))}");
            }

            var infoResponse = results[0].Response.ToObject<BlockchainInfo>();
            var miningInfoResponse = results[1].Response.ToObject<MiningInfo>();
            var networkInfoResponse = results[2].Response.ToObject<NetworkInfo>();

            BlockchainStats.BlockHeight = infoResponse.Blocks;
            BlockchainStats.NetworkHashrate = miningInfoResponse.NetworkHashps;
            BlockchainStats.ConnectedPeers = networkInfoResponse.Connections;
        }

        protected virtual async Task<(bool Accepted, string CoinbaseTransaction)> SubmitBlockAsync(Share share, string blockHex)
        {
            // execute command batch
            var results = await daemon.ExecuteBatchAnyAsync(
                hasSubmitBlockMethod
                    ? new DaemonCmd(BitcoinCommands.SubmitBlock, new[] { blockHex })
                    : new DaemonCmd(BitcoinCommands.GetBlockTemplate, new { mode = "submit", data = blockHex }),
                new DaemonCmd(BitcoinCommands.GetBlock, new[] { share.BlockHash }));

            // did submission succeed?
            var submitResult = results[0];
            var submitError = submitResult.Error?.Message ??
                submitResult.Error?.Code.ToString(CultureInfo.InvariantCulture) ??
                submitResult.Response?.ToString();

            if (!string.IsNullOrEmpty(submitError))
            {
                logger.Warn(() => $"[{LogCat}] Block {share.BlockHeight} submission failed with: {submitError}");
                notificationService.NotifyAdmin("Block submission failed", $"Pool {poolConfig.Id} {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}failed to submit block {share.BlockHeight}: {submitError}");
                return (false, null);
            }

            // was it accepted?
            var acceptResult = results[1];
            var block = acceptResult.Response?.ToObject<DaemonResponses.Block>();
            var accepted = acceptResult.Error == null && block?.Hash == share.BlockHash;

            if (!accepted)
            {
                logger.Warn(() => $"[{LogCat}] Block {share.BlockHeight} submission failed for pool {poolConfig.Id} because block was not found after submission");
                notificationService.NotifyAdmin($"[{share.PoolId.ToUpper()}]-[{share.Source}] Block submission failed", $"[{share.PoolId.ToUpper()}]-[{share.Source}] Block {share.BlockHeight} submission failed for pool {poolConfig.Id} because block was not found after submission");
            }

            return (accepted, block?.Transactions.FirstOrDefault());
        }

        protected virtual void SetupCrypto()
        {
            var coinProps = BitcoinProperties.GetCoinProperties(poolConfig.Coin.Type, poolConfig.Coin.Algorithm);

            if (coinProps == null)
                logger.ThrowLogPoolStartupException($"Coin Type '{poolConfig.Coin.Type}' not supported by this Job Manager", LogCat);

            coinbaseHasher = coinProps.CoinbaseHasher;
            headerHasher = coinProps.HeaderHasher;
            blockHasher = !isPoS ? coinProps.BlockHasher : (coinProps.PoSBlockHasher ?? coinProps.BlockHasher);
            ShareMultiplier = coinProps.ShareMultiplier;
        }

        protected virtual async Task<bool> AreDaemonsHealthyLegacyAsync()
        {
            var responses = await daemon.ExecuteCmdAllAsync<DaemonInfo>(BitcoinCommands.GetInfo);

            if (responses.Where(x => x.Error?.InnerException?.GetType() == typeof(DaemonClientException))
                .Select(x => (DaemonClientException)x.Error.InnerException)
                .Any(x => x.Code == HttpStatusCode.Unauthorized))
                logger.ThrowLogPoolStartupException($"Daemon reports invalid credentials", LogCat);

            return responses.All(x => x.Error == null);
        }

        protected virtual async Task<bool> AreDaemonsConnectedLegacyAsync()
        {
            var response = await daemon.ExecuteCmdAnyAsync<DaemonInfo>(BitcoinCommands.GetInfo);

            return response.Error == null && response.Response.Connections > 0;
        }

        protected virtual async Task ShowDaemonSyncProgressLegacyAsync()
        {
            var infos = await daemon.ExecuteCmdAllAsync<DaemonInfo>(BitcoinCommands.GetInfo);

            if (infos.Length > 0)
            {
                var blockCount = infos
                    .Max(x => x.Response?.Blocks);

                if (blockCount.HasValue)
                {
                    // get list of peers and their highest block height to compare to ours
                    var peerInfo = await daemon.ExecuteCmdAnyAsync<PeerInfo[]>(BitcoinCommands.GetPeerInfo);
                    var peers = peerInfo.Response;

                    if (peers != null && peers.Length > 0)
                    {
                        var totalBlocks = peers.Max(x => x.StartingHeight);
                        var percent = totalBlocks > 0 ? (double)blockCount / totalBlocks * 100 : 0;
                        logger.Info(() => $"[{LogCat}] Daemons have downloaded {percent:0.00}% of blockchain from {peers.Length} peers");
                    }
                }
            }
        }

        private async Task UpdateNetworkStatsLegacyAsync()
        {
            logger.LogInvoke(LogCat);

            var results = await daemon.ExecuteBatchAnyAsync(
                new DaemonCmd(BitcoinCommands.GetMiningInfo),
                new DaemonCmd(BitcoinCommands.GetConnectionCount)
            );

            if (results.Any(x => x.Error != null))
            {
                var errors = results.Where(x => x.Error != null).ToArray();

                if (errors.Any())
                    logger.Warn(() => $"[{LogCat}] Error(s) refreshing network stats: {string.Join(", ", errors.Select(y => y.Error.Message))}");
            }

            var miningInfoResponse = results[0].Response.ToObject<MiningInfo>();
            var connectionCountResponse = results[1].Response.ToObject<object>();

            BlockchainStats.BlockHeight = miningInfoResponse.Blocks;
            //BlockchainStats.NetworkHashrate = miningInfoResponse.NetworkHashps;
            BlockchainStats.ConnectedPeers = (int) (long) connectionCountResponse;
        }

        #region API-Surface

        public BitcoinNetworkType NetworkType => networkType;

        public IObservable<object> Jobs { get; private set; }

        public virtual async Task<bool> ValidateAddressAsync(string address)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

            var result = await daemon.ExecuteCmdAnyAsync<ValidateAddressResponse>(
                BitcoinCommands.ValidateAddress, new[] { address });

            return result.Response != null && result.Response.IsValid;
        }

        public virtual object[] GetSubscriberData(StratumClient worker)
        {
            Contract.RequiresNonNull(worker, nameof(worker));

            var context = worker.GetContextAs<BitcoinWorkerContext>();

            // assign unique ExtraNonce1 to worker (miner)
            context.ExtraNonce1 = extraNonceProvider.Next();

            // setup response data
            var responseData = new object[]
            {
                context.ExtraNonce1,
                BitcoinConstants.ExtranoncePlaceHolderLength - ExtranonceBytes,
            };

            return responseData;
        }

        public string[] GetTransactions(StratumClient worker, object requestParams)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.RequiresNonNull(requestParams, nameof(requestParams));

            if (!(requestParams is object[] queryParams))
                throw new StratumException(StratumError.Other, "invalid params");

            // extract params
            var jobId = queryParams[0] as string;

            TJob job;

            lock(jobLock)
            {
                job = validJobs.FirstOrDefault(x => x.JobId == jobId);
            }

            if (job == null)
                throw new StratumException(StratumError.JobNotFound, "job not found");

            return job.BlockTemplate.Transactions.Select(x => x.Data).ToArray();
        }

        public virtual async Task<Share> SubmitShareAsync(StratumClient worker, object submission,
            double stratumDifficultyBase)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.RequiresNonNull(submission, nameof(submission));

            logger.LogInvoke(LogCat, new[] { worker.ConnectionId });

            if (!(submission is object[] submitParams))
                throw new StratumException(StratumError.Other, "invalid params");

            var context = worker.GetContextAs<BitcoinWorkerContext>();

            // extract params
            var workerValue = (submitParams[0] as string)?.Trim();
            var jobId = submitParams[1] as string;
            var extraNonce2 = submitParams[2] as string;
            var nTime = submitParams[3] as string;
            var nonce = submitParams[4] as string;

            if (string.IsNullOrEmpty(workerValue))
                throw new StratumException(StratumError.Other, "missing or invalid workername");

            TJob job;

            lock(jobLock)
            {
                job = validJobs.FirstOrDefault(x => x.JobId == jobId);
            }

            if (job == null)
                throw new StratumException(StratumError.JobNotFound, "job not found");

            // extract worker/miner/payoutid
            var split = workerValue.Split('.');
            var minerName = split[0];
            var workerName = split.Length > 1 ? split[1] : null;

            // validate & process
            var (share, blockHex) = job.ProcessShare(worker, extraNonce2, nTime, nonce);

            // enrich share with common data
            share.PoolId = poolConfig.Id;
            share.IpAddress = worker.RemoteEndpoint.Address.ToString();
            share.Miner = minerName;
            share.Worker = workerName;
            share.UserAgent = context.UserAgent;
            share.Source = clusterConfig.ClusterName;
            share.Created = clock.Now;

            // if block candidate, submit & check if accepted by network
            if (share.IsBlockCandidate)
            {
                logger.Info(() => $"[{LogCat}] Submitting block {share.BlockHeight} [{share.BlockHash}]");

                var acceptResponse = await SubmitBlockAsync(share, blockHex);

                // is it still a block candidate?
                share.IsBlockCandidate = acceptResponse.Accepted;

                if (share.IsBlockCandidate)
                {
                    logger.Info(() => $"[{LogCat}] Daemon accepted block {share.BlockHeight} [{share.BlockHash}] submitted by {minerName}");

                    blockSubmissionSubject.OnNext(Unit.Default);

                    // persist the coinbase transaction-hash to allow the payment processor
                    // to verify later on that the pool has received the reward for the block
                    share.TransactionConfirmationData = acceptResponse.CoinbaseTransaction;
                }

                else
                {
                    // clear fields that no longer apply
                    share.TransactionConfirmationData = null;
                }
            }

            return share;
        }

        public BlockchainStats BlockchainStats { get; } = new BlockchainStats();
        public double ShareMultiplier { get; private set; }

        #endregion // API-Surface

        #region Overrides

        protected override string LogCat => "Bitcoin Job Manager";

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<BitcoinPoolConfigExtra>();
            extraPoolPaymentProcessingConfig = poolConfig.PaymentProcessing?.Extra?.SafeExtensionDataAs<BitcoinPoolPaymentProcessingConfigExtra>();

            if (extraPoolConfig?.MaxActiveJobs.HasValue == true)
                maxActiveJobs = extraPoolConfig.MaxActiveJobs.Value;

            hasLegacyDaemon = extraPoolConfig?.HasLegacyDaemon == true;

            base.Configure(poolConfig, clusterConfig);
        }

        protected override void ConfigureDaemons()
        {
            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            daemon = new DaemonClient(jsonSerializerSettings);
            daemon.Configure(poolConfig.Daemons);
        }

        protected override async Task<bool> AreDaemonsHealthyAsync()
        {
            if (hasLegacyDaemon)
                return await AreDaemonsHealthyLegacyAsync();

            var responses = await daemon.ExecuteCmdAllAsync<BlockchainInfo>(BitcoinCommands.GetBlockchainInfo);

            if (responses.Where(x => x.Error?.InnerException?.GetType() == typeof(DaemonClientException))
                .Select(x => (DaemonClientException)x.Error.InnerException)
                .Any(x => x.Code == HttpStatusCode.Unauthorized))
                logger.ThrowLogPoolStartupException($"Daemon reports invalid credentials", LogCat);

            return responses.All(x => x.Error == null);
        }

        protected override async Task<bool> AreDaemonsConnectedAsync()
        {
            if (hasLegacyDaemon)
                return await AreDaemonsConnectedLegacyAsync();

            var response = await daemon.ExecuteCmdAnyAsync<NetworkInfo>(BitcoinCommands.GetNetworkInfo);

            return response.Error == null && response.Response?.Connections > 0;
        }

        protected override async Task EnsureDaemonsSynchedAsync()
        {
            var syncPendingNotificationShown = false;

            while(true)
            {
                var responses = await daemon.ExecuteCmdAllAsync<BlockTemplate>(
                    BitcoinCommands.GetBlockTemplate, getBlockTemplateParams);

                var isSynched = responses.All(x => x.Error == null);

                if (isSynched)
                {
                    logger.Info(() => $"[{LogCat}] All daemons synched with blockchain");
                    break;
                }

                if (!syncPendingNotificationShown)
                {
                    logger.Info(() => $"[{LogCat}] Daemons still syncing with network. Manager will be started once synced");
                    syncPendingNotificationShown = true;
                }

                await ShowDaemonSyncProgressAsync();

                // delay retry by 5s
                await Task.Delay(5000);
            }
        }

        protected override async Task PostStartInitAsync()
        {
            var commands = new[]
            {
                new DaemonCmd(BitcoinCommands.ValidateAddress, new[] { poolConfig.Address }),
                new DaemonCmd(BitcoinCommands.SubmitBlock),
                new DaemonCmd(!hasLegacyDaemon ? BitcoinCommands.GetBlockchainInfo : BitcoinCommands.GetInfo),
                new DaemonCmd(BitcoinCommands.GetDifficulty),
            };

            var results = await daemon.ExecuteBatchAnyAsync(commands);

            if (results.Any(x => x.Error != null))
            {
                var resultList = results.ToList();
                var errors = results.Where(x => x.Error != null && commands[resultList.IndexOf(x)].Method != BitcoinCommands.SubmitBlock)
                    .ToArray();

                if (errors.Any())
                    logger.ThrowLogPoolStartupException($"Init RPC failed: {string.Join(", ", errors.Select(y => y.Error.Message))}", LogCat);
            }

            // extract results
            var validateAddressResponse = results[0].Response.ToObject<ValidateAddressResponse>();
            var submitBlockResponse = results[1];
            var blockchainInfoResponse = !hasLegacyDaemon ? results[2].Response.ToObject<BlockchainInfo>() : null;
            var daemonInfoResponse = hasLegacyDaemon ? results[2].Response.ToObject<DaemonInfo>() : null;
            var difficultyResponse = results[3].Response.ToObject<JToken>();

            // ensure pool owns wallet
            if (!validateAddressResponse.IsValid)
                logger.ThrowLogPoolStartupException($"Daemon reports pool-address '{poolConfig.Address}' as invalid", LogCat);

            if (clusterConfig.PaymentProcessing?.Enabled == true && !validateAddressResponse.IsMine)
                logger.ThrowLogPoolStartupException($"Daemon does not own pool-address '{poolConfig.Address}'", LogCat);

            isPoS = difficultyResponse.Values().Any(x => x.Path == "proof-of-stake");

            // Create pool address script from response
            if (!isPoS)
            {
                // bitcoincashd returns a different address than what was passed in
                if(!validateAddressResponse.Address.StartsWith("bitcoincash:"))
                    poolAddressDestination = AddressToDestination(validateAddressResponse.Address);
                else
                    poolAddressDestination = AddressToDestination(poolConfig.Address);
            }

            else
                poolAddressDestination = new PubKey(validateAddressResponse.PubKey);

            // chain detection
            if (!hasLegacyDaemon)
            {
                if (blockchainInfoResponse.Chain.ToLower() == "test")
                    networkType = BitcoinNetworkType.Test;
                else if (blockchainInfoResponse.Chain.ToLower() == "regtest")
                    networkType = BitcoinNetworkType.RegTest;
                else
                    networkType = BitcoinNetworkType.Main;
            }

            else
                networkType = daemonInfoResponse.Testnet ? BitcoinNetworkType.Test : BitcoinNetworkType.Main;

            if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
                ConfigureRewards();

            // update stats
            BlockchainStats.NetworkType = networkType.ToString();
            BlockchainStats.RewardType = isPoS ? "POS" : "POW";

            // block submission RPC method
            if (submitBlockResponse.Error?.Message?.ToLower() == "method not found")
                hasSubmitBlockMethod = false;
            else if (submitBlockResponse.Error?.Code == -1)
                hasSubmitBlockMethod = true;
            else
                logger.ThrowLogPoolStartupException($"Unable detect block submission RPC method", LogCat);

            if(!hasLegacyDaemon)
                await UpdateNetworkStatsAsync();
            else
                await UpdateNetworkStatsLegacyAsync();

            SetupCrypto();
            SetupJobUpdates();
        }

        protected virtual IDestination AddressToDestination(string address)
        {
            return BitcoinUtils.AddressToDestination(address);
        }

        protected virtual void ConfigureRewards()
        {
            // Donation to MiningCore development
            if (networkType == BitcoinNetworkType.Main &&
                DevDonation.Addresses.TryGetValue(poolConfig.Coin.Type, out var address))
            {
                poolConfig.RewardRecipients = poolConfig.RewardRecipients.Concat(new[]
                {
                    new RewardRecipient
                    {
                        Address = address,
                        Percentage = DevDonation.Percent
                    }
                }).ToArray();
            }
        }

        protected virtual async Task<(bool IsNew, bool Force)> UpdateJob(bool forceUpdate, string via = null)
        {
            logger.LogInvoke(LogCat);

            try
            {
                var response = await GetBlockTemplateAsync();

                // may happen if daemon is currently not connected to peers
                if (response.Error != null)
                {
                    logger.Warn(() => $"[{LogCat}] Unable to update job. Daemon responded with: {response.Error.Message} Code {response.Error.Code}");
                    return (false, forceUpdate);
                }

                var blockTemplate = response.Response;

                var job = currentJob;
                var isNew = job == null ||
                    (blockTemplate != null &&
                    job.BlockTemplate?.PreviousBlockhash != blockTemplate.PreviousBlockhash &&
                    blockTemplate.Height > job.BlockTemplate?.Height);

                if (isNew || forceUpdate)
                {
                    job = new TJob();

                    job.Init(blockTemplate, NextJobId(),
                        poolConfig, clusterConfig, clock, poolAddressDestination, networkType, isPoS,
                        ShareMultiplier, extraPoolPaymentProcessingConfig?.BlockrewardMultiplier ?? 1.0m,
                        coinbaseHasher, headerHasher, blockHasher);

                    lock (jobLock)
                    {
                        if (isNew)
                        {
                            if(via != null)
                                logger.Info(()=> $"[{LogCat}] Detected new block {blockTemplate.Height} via {via}");
                            else
                                logger.Info(() => $"[{LogCat}] Detected new block {blockTemplate.Height}");

                            validJobs.Clear();

                            // update stats
                            BlockchainStats.LastNetworkBlockTime = clock.Now;
                            BlockchainStats.BlockHeight = blockTemplate.Height;
                            BlockchainStats.NetworkDifficulty = job.Difficulty;
                        }

                        else
                        {
                            // trim active jobs
                            while(validJobs.Count > maxActiveJobs - 1)
                                validJobs.RemoveAt(0);
                        }

                        validJobs.Add(job);
                    }

                    currentJob = job;
                }

                return (isNew, forceUpdate);
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Error during {nameof(UpdateJob)}");
            }

            return (false, forceUpdate);
        }

        protected virtual object GetJobParamsForStratum(bool isNew)
        {
            var job = currentJob;
            return job?.GetJobParams(isNew);
        }

        #endregion // Overrides
    }
}
