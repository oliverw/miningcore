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
using System.Linq;
using System.Net;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Autofac;
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
        protected const int MaxActiveJobs = 4;
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
            jobRebroadcastTimeout = TimeSpan.FromSeconds(poolConfig.JobRebroadcastTimeout);

            // periodically update block-template from daemon
            var newJobs = Observable.Interval(TimeSpan.FromMilliseconds(poolConfig.BlockRefreshInterval))
                .Select(_ => Observable.FromAsync(() => UpdateJob(false)))
                .Concat()
                .Do(isNew =>
                {
                    if (isNew)
                        logger.Info(() => $"[{LogCat}] New block {currentJob.BlockTemplate.Height} detected");
                })
                .Where(isNew => isNew)
                .Publish()
                .RefCount();

            // if there haven't been any new jobs for a while, force an update
            var forcedNewJobs = Observable.Timer(jobRebroadcastTimeout)
                .TakeUntil(newJobs) // cancel timeout if an actual new job has been detected
                .Do(_ => logger.Debug(() => $"[{LogCat}] No new blocks for {jobRebroadcastTimeout.TotalSeconds} seconds - updating transactions & rebroadcasting work"))
                .Select(x => Observable.FromAsync(() => UpdateJob(true)))
                .Concat()
                .Repeat();

            Jobs = newJobs.Merge(forcedNewJobs)
                .Select(GetJobParamsForStratum);
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

        protected virtual async Task<(bool Accepted, string CoinbaseTransaction)> SubmitBlockAsync(BitcoinShare share)
        {
            // execute command batch
            var results = await daemon.ExecuteBatchAnyAsync(
                hasSubmitBlockMethod
                    ? new DaemonCmd(BitcoinCommands.SubmitBlock, new[] { share.BlockHex })
                    : new DaemonCmd(BitcoinCommands.GetBlockTemplate, new { mode = "submit", data = share.BlockHex }),
                new DaemonCmd(BitcoinCommands.GetBlock, new[] { share.BlockHash }));

            // did submission succeed?
            var submitResult = results[0];
            var submitError = submitResult.Error?.Message ?? submitResult.Response?.ToString();

            if (!string.IsNullOrEmpty(submitError))
            {
                logger.Warn(() => $"[{LogCat}] Block {share.BlockHeight} submission failed with: {submitError}");
                notificationService.NotifyAdmin("Block submission failed", $"Block {share.BlockHeight} submission failed with: {submitError}");

                return (false, null);
            }

            // was it accepted?
            var acceptResult = results[1];
            var block = acceptResult.Response?.ToObject<DaemonResponses.Block>();
            var accepted = acceptResult.Error == null && block?.Hash == share.BlockHash;

            if (!accepted)
            {
                logger.Warn(() => $"[{LogCat}] Block {share.BlockHeight} submission failed because block was not found after submission");
                notificationService.NotifyAdmin("Block submission failed", $"Block {share.BlockHeight} submission failed because block was not found after submission");
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
            blockHasher = !isPoS ? coinProps.BlockHasher : coinProps.PoSBlockHasher;
            ShareMultiplier = coinProps.ShareMultiplier;
        }

        #region API-Surface

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

        public virtual async Task<IShare> SubmitShareAsync(StratumClient worker, object submission,
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
            var share = job.ProcessShare(worker, extraNonce2, nTime, nonce);

            // if block candidate, submit & check if accepted by network
            if (share.IsBlockCandidate)
            {
                logger.Info(() => $"[{LogCat}] Submitting block {share.BlockHeight} [{share.BlockHash}]");

                var acceptResponse = await SubmitBlockAsync(share);

                // is it still a block candidate?
                share.IsBlockCandidate = acceptResponse.Accepted;

                if (share.IsBlockCandidate)
                {
                    logger.Info(() => $"[{LogCat}] Daemon accepted block {share.BlockHeight} [{share.BlockHash}]");

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

            // enrich share with common data
            share.PoolId = poolConfig.Id;
            share.IpAddress = worker.RemoteEndpoint.Address.ToString();
            share.Miner = minerName;
            share.Worker = workerName;
            share.UserAgent = context.UserAgent;
            share.NetworkDifficulty = job.Difficulty;
            share.Difficulty = share.Difficulty / ShareMultiplier;
            share.Created = clock.Now;

            return share;
        }

        public async Task UpdateNetworkStatsAsync()
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
            BlockchainStats.NetworkDifficulty = miningInfoResponse.Difficulty;
            BlockchainStats.NetworkHashRate = miningInfoResponse.NetworkHashps;
            BlockchainStats.ConnectedPeers = networkInfoResponse.Connections;
        }

        public BlockchainStats BlockchainStats { get; } = new BlockchainStats();
        public double ShareMultiplier { get; private set; }

        #endregion // API-Surface

        #region Overrides

        protected override string LogCat => "Bitcoin Job Manager";

        protected override void ConfigureDaemons()
        {
            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            daemon = new DaemonClient(jsonSerializerSettings);
            daemon.Configure(poolConfig.Daemons);
        }

        protected override async Task<bool> AreDaemonsHealthy()
        {
            var responses = await daemon.ExecuteCmdAllAsync<BlockchainInfo>(BitcoinCommands.GetBlockchainInfo);

            if (responses.Where(x => x.Error?.InnerException?.GetType() == typeof(DaemonClientException))
                .Select(x => (DaemonClientException) x.Error.InnerException)
                .Any(x => x.Code == HttpStatusCode.Unauthorized))
                logger.ThrowLogPoolStartupException($"Daemon reports invalid credentials", LogCat);

            return responses.All(x => x.Error == null);
        }

        protected override async Task<bool> AreDaemonsConnected()
        {
            var response = await daemon.ExecuteCmdAnyAsync<NetworkInfo>(BitcoinCommands.GetNetworkInfo);

            return response.Error == null && response.Response.Connections > 0;
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
                new DaemonCmd(BitcoinCommands.GetDifficulty),
                new DaemonCmd(BitcoinCommands.SubmitBlock),
                new DaemonCmd(BitcoinCommands.GetBlockchainInfo)
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
            var difficultyResponse = results[1].Response.ToObject<JToken>();
            var submitBlockResponse = results[2];
            var blockchainInfoResponse = results[3].Response.ToObject<BlockchainInfo>();

            // ensure pool owns wallet
            if (!validateAddressResponse.IsValid)
                logger.ThrowLogPoolStartupException($"Daemon reports pool-address '{poolConfig.Address}' as invalid", LogCat);

            if (!validateAddressResponse.IsMine)
                logger.ThrowLogPoolStartupException($"Daemon does not own pool-address '{poolConfig.Address}'", LogCat);

            isPoS = difficultyResponse.Values().Any(x => x.Path == "proof-of-stake");

            // Create pool address script from response
            if (isPoS)
                poolAddressDestination = new PubKey(validateAddressResponse.PubKey);
            else
                poolAddressDestination = AddressToDestination(validateAddressResponse.Address);

            // chain detection
            if (blockchainInfoResponse.Chain.ToLower() == "test")
                networkType = BitcoinNetworkType.Test;
            else if (blockchainInfoResponse.Chain.ToLower() == "regtest")
                networkType = BitcoinNetworkType.RegTest;
            else
                networkType = BitcoinNetworkType.Main;

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

            await UpdateNetworkStatsAsync();

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
            var devDonation = clusterConfig.DevDonation ?? 0.15m;

            if (devDonation > 0)
            {
                string address = null;

                if (networkType == BitcoinNetworkType.Main &&
                    KnownAddresses.DevFeeAddresses.ContainsKey(poolConfig.Coin.Type))
                    address = KnownAddresses.DevFeeAddresses[poolConfig.Coin.Type];

                if (!string.IsNullOrEmpty(address))
                {
                    poolConfig.RewardRecipients = poolConfig.RewardRecipients.Concat(new[]
                    {
                        new RewardRecipient
                        {
                            Address = address,
                            Percentage = devDonation,
                        }
                    }).ToArray();
                }
            }
        }

        protected virtual async Task<bool> UpdateJob(bool forceUpdate)
        {
            logger.LogInvoke(LogCat);

            try
            {
                var response = await GetBlockTemplateAsync();

                // may happen if daemon is currently not connected to peers
                if (response.Error != null)
                {
                    logger.Warn(() => $"[{LogCat}] Unable to update job. Daemon responded with: {response.Error.Message} Code {response.Error.Code}");
                    return false;
                }

                var blockTemplate = response.Response;

                var job = currentJob;
                var isNew = job == null ||
                            job.BlockTemplate?.PreviousBlockhash != blockTemplate.PreviousBlockhash ||
                            job.BlockTemplate?.Height < blockTemplate.Height;

                if (isNew || forceUpdate)
                {
                    job = new TJob();

                    job.Init(blockTemplate, NextJobId(),
                        poolConfig, clusterConfig, clock, poolAddressDestination, networkType, isPoS,
                        ShareMultiplier,
                        coinbaseHasher, headerHasher, blockHasher);

                    // update stats
                    if (isNew)
                        BlockchainStats.LastNetworkBlockTime = clock.Now;

                    lock (jobLock)
                    {
                        validJobs.Add(job);

                        // trim active jobs
                        while (validJobs.Count > MaxActiveJobs)
                            validJobs.RemoveAt(0);
                    }

                    currentJob = job;
                }

                return isNew;
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Error during {nameof(UpdateJob)}");
            }

            return false;
        }

        protected virtual object GetJobParamsForStratum(bool isNew)
        {
            var job = currentJob;
            return job?.GetJobParams(isNew);
        }

        #endregion // Overrides
    }
}
