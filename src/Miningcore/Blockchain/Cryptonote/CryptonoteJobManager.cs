using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Cryptonote.Configuration;
using Miningcore.Blockchain.Cryptonote.DaemonRequests;
using Miningcore.Blockchain.Cryptonote.DaemonResponses;
using Miningcore.Blockchain.Cryptonote.StratumRequests;
using Miningcore.Configuration;
using Miningcore.DaemonInterface;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Native;
using Miningcore.Notifications.Messages;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using MoreLinq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Blockchain.Cryptonote
{
    public class CryptonoteJobManager : JobManagerBase<CryptonoteJob>
    {
        public CryptonoteJobManager(
            IComponentContext ctx,
            IMasterClock clock,
            IMessageBus messageBus) :
            base(ctx, messageBus)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));

            this.clock = clock;
        }

        private byte[] instanceId;
        private DaemonEndpointConfig[] daemonEndpoints;
        private DaemonClient daemon;
        private DaemonClient walletDaemon;
        private readonly IMasterClock clock;
        private CryptonoteNetworkType networkType;
        private CryptonotePoolConfigExtra extraPoolConfig;
        private LibRandomX.randomx_flags? randomXFlagsOverride;
        private LibRandomX.randomx_flags? randomXFlagsAdd;
        private string currentSeedHash;
        private string randomXRealm;
        private ulong poolAddressBase58Prefix;
        private DaemonEndpointConfig[] walletDaemonEndpoints;
        private CryptonoteCoinTemplate coin;

        protected async Task<bool> UpdateJob(string via = null, string json = null)
        {
            logger.LogInvoke();

            try
            {
                var response = string.IsNullOrEmpty(json) ? await GetBlockTemplateAsync() : GetBlockTemplateFromJson(json);

                // may happen if daemon is currently not connected to peers
                if(response.Error != null)
                {
                    logger.Warn(() => $"Unable to update job. Daemon responded with: {response.Error.Message} Code {response.Error.Code}");
                    return false;
                }

                var blockTemplate = response.Response;
                var job = currentJob;
                var newHash = blockTemplate.Blob.HexToByteArray().Slice(7, 32).ToHexString();

                var isNew = job == null || newHash != job.PrevHash;

                if(isNew)
                {
                    messageBus.NotifyChainHeight(poolConfig.Id, blockTemplate.Height, poolConfig.Template);

                    if(via != null)
                        logger.Info(() => $"Detected new block {blockTemplate.Height} [{via}]");
                    else
                        logger.Info(() => $"Detected new block {blockTemplate.Height}");

                    // detect seed hash change
                    if(currentSeedHash != blockTemplate.SeedHash)
                    {
                        logger.Info(()=> $"Detected new seed hash {blockTemplate.SeedHash} starting @ height {blockTemplate.Height}");

                        if(poolConfig.EnableInternalStratum == true)
                        {
                            LibRandomX.WithLock(() =>
                            {
                                // delete old seed
                                if(currentSeedHash != null)
                                    LibRandomX.DeleteSeed(randomXRealm, currentSeedHash);

                                // activate new one
                                currentSeedHash = blockTemplate.SeedHash;
                                LibRandomX.CreateSeed(randomXRealm, currentSeedHash, randomXFlagsOverride, randomXFlagsAdd, extraPoolConfig.RandomXVMCount);
                            });
                        }

                        else
                            currentSeedHash = blockTemplate.SeedHash;
                    }

                    // init job
                    job = new CryptonoteJob(blockTemplate, instanceId, NextJobId(), coin, poolConfig, clusterConfig, newHash, randomXRealm);
                    currentJob = job;

                    // update stats
                    BlockchainStats.LastNetworkBlockTime = clock.Now;
                    BlockchainStats.BlockHeight = job.BlockTemplate.Height;
                    BlockchainStats.NetworkDifficulty = job.BlockTemplate.Difficulty;
                    BlockchainStats.NextNetworkTarget = "";
                    BlockchainStats.NextNetworkBits = "";
                }

                else
                {
                    if(via != null)
                        logger.Debug(() => $"Template update {blockTemplate.Height} [{via}]");
                    else
                        logger.Debug(() => $"Template update {blockTemplate.Height}");
                }

                return isNew;
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"Error during {nameof(UpdateJob)}");
            }

            return false;
        }

        private async Task<DaemonResponse<GetBlockTemplateResponse>> GetBlockTemplateAsync()
        {
            logger.LogInvoke();

            var request = new GetBlockTemplateRequest
            {
                WalletAddress = poolConfig.Address,
                ReserveSize = CryptonoteConstants.ReserveSize
            };

            return await daemon.ExecuteCmdAnyAsync<GetBlockTemplateResponse>(logger, CryptonoteCommands.GetBlockTemplate, request);
        }

        private DaemonResponse<GetBlockTemplateResponse> GetBlockTemplateFromJson(string json)
        {
            logger.LogInvoke();

            var result = JsonConvert.DeserializeObject<JsonRpcResponse>(json);

            return new DaemonResponse<GetBlockTemplateResponse>
            {
                Response = result.ResultAs<GetBlockTemplateResponse>(),
            };
        }

        private async Task ShowDaemonSyncProgressAsync()
        {
            var infos = await daemon.ExecuteCmdAllAsync<GetInfoResponse>(logger, CryptonoteCommands.GetInfo);
            var firstValidResponse = infos.FirstOrDefault(x => x.Error == null && x.Response != null)?.Response;

            if(firstValidResponse != null)
            {
                var lowestHeight = infos.Where(x => x.Error == null && x.Response != null)
                    .Min(x => x.Response.Height);

                var totalBlocks = firstValidResponse.TargetHeight;
                var percent = (double) lowestHeight / totalBlocks * 100;

                logger.Info(() => $"Daemons have downloaded {percent:0.00}% of blockchain from {firstValidResponse.OutgoingConnectionsCount} peers");
            }
        }

        private async Task UpdateNetworkStatsAsync()
        {
            logger.LogInvoke();

            try
            {
                var infoResponse = await daemon.ExecuteCmdAnyAsync(logger, CryptonoteCommands.GetInfo);

                if(infoResponse.Error != null)
                    logger.Warn(() => $"Error(s) refreshing network stats: {infoResponse.Error.Message} (Code {infoResponse.Error.Code})");

                if(infoResponse.Response != null)
                {
                    var info = infoResponse.Response.ToObject<GetInfoResponse>();

                    BlockchainStats.NetworkHashrate = info.Target > 0 ? (double) info.Difficulty / info.Target : 0;
                    BlockchainStats.ConnectedPeers = info.OutgoingConnectionsCount + info.IncomingConnectionsCount;
                }
            }

            catch(Exception e)
            {
                logger.Error(e);
            }
        }

        private async Task<bool> SubmitBlockAsync(Share share, string blobHex, string blobHash)
        {
            var response = await daemon.ExecuteCmdAnyAsync<SubmitResponse>(logger, CryptonoteCommands.SubmitBlock, new[] { blobHex });

            if(response.Error != null || response?.Response?.Status != "OK")
            {
                var error = response.Error?.Message ?? response.Response?.Status;

                logger.Warn(() => $"Block {share.BlockHeight} [{blobHash[..6]}] submission failed with: {error}");
                messageBus.SendMessage(new AdminNotification("Block submission failed", $"Pool {poolConfig.Id} {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}failed to submit block {share.BlockHeight}: {error}"));
                return false;
            }

            return true;
        }

        #region API-Surface

        public IObservable<Unit> Blocks { get; private set; }

        public CryptonoteCoinTemplate Coin => coin;

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

            logger = LogUtil.GetPoolScopedLogger(typeof(JobManagerBase<CryptonoteJob>), poolConfig);
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;
            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<CryptonotePoolConfigExtra>();
            coin = poolConfig.Template.As<CryptonoteCoinTemplate>();

            if(poolConfig.EnableInternalStratum == true)
            {
                randomXRealm = !string.IsNullOrEmpty(extraPoolConfig.RandomXRealm) ? extraPoolConfig.RandomXRealm : poolConfig.Id;
                randomXFlagsOverride = MakeRandomXFlags(extraPoolConfig.RandomXFlagsOverride);
                randomXFlagsAdd = MakeRandomXFlags(extraPoolConfig.RandomXFlagsAdd);
            }

            // extract standard daemon endpoints
            daemonEndpoints = poolConfig.Daemons
                .Where(x => string.IsNullOrEmpty(x.Category))
                .Select(x =>
                {
                    if(string.IsNullOrEmpty(x.HttpPath))
                        x.HttpPath = CryptonoteConstants.DaemonRpcLocation;

                    return x;
                })
                .ToArray();

            if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
            {
                // extract wallet daemon endpoints
                walletDaemonEndpoints = poolConfig.Daemons
                    .Where(x => x.Category?.ToLower() == CryptonoteConstants.WalletDaemonCategory)
                    .Select(x =>
                    {
                        if(string.IsNullOrEmpty(x.HttpPath))
                            x.HttpPath = CryptonoteConstants.DaemonRpcLocation;

                        return x;
                    })
                    .ToArray();

                if(walletDaemonEndpoints.Length == 0)
                    logger.ThrowLogPoolStartupException("Wallet-RPC daemon is not configured (Daemon configuration for monero-pools require an additional entry of category \'wallet' pointing to the wallet daemon)");
            }

            ConfigureDaemons();
        }

        public bool ValidateAddress(string address)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

            var addressPrefix = LibCryptonote.DecodeAddress(address);
            var addressIntegratedPrefix = LibCryptonote.DecodeIntegratedAddress(address);
            var coin = poolConfig.Template.As<CryptonoteCoinTemplate>();

            switch(networkType)
            {
                case CryptonoteNetworkType.Main:
                    if(addressPrefix != coin.AddressPrefix &&
                        addressIntegratedPrefix != coin.AddressPrefixIntegrated)
                        return false;
                    break;

                case CryptonoteNetworkType.Test:
                    if(addressPrefix != coin.AddressPrefixTestnet &&
                        addressIntegratedPrefix != coin.AddressPrefixIntegratedTestnet)
                        return false;
                    break;
            }

            return true;
        }

        public BlockchainStats BlockchainStats { get; } = new();

        public void PrepareWorkerJob(CryptonoteWorkerJob workerJob, out string blob, out string target)
        {
            blob = null;
            target = null;

            var job = currentJob;

            if(job != null)
            {
                lock(job)
                {
                    job.PrepareWorkerJob(workerJob, out blob, out target);
                }
            }
        }

        public async ValueTask<Share> SubmitShareAsync(StratumConnection worker,
            CryptonoteSubmitShareRequest request, CryptonoteWorkerJob workerJob, double stratumDifficultyBase, CancellationToken ct)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.RequiresNonNull(request, nameof(request));

            logger.LogInvoke(new[] { worker.ConnectionId });
            var context = worker.ContextAs<CryptonoteWorkerContext>();

            var job = currentJob;
            if(workerJob.Height != job?.BlockTemplate.Height)
                throw new StratumException(StratumError.MinusOne, "block expired");

            // validate & process
            var (share, blobHex) = job.ProcessShare(request.Nonce, workerJob.ExtraNonce, request.Hash, worker);

            // enrich share with common data
            share.PoolId = poolConfig.Id;
            share.IpAddress = worker.RemoteEndpoint.Address.ToString();
            share.Miner = context.Miner;
            share.Worker = context.Worker;
            share.UserAgent = context.UserAgent;
            share.Source = clusterConfig.ClusterName;
            share.NetworkDifficulty = job.BlockTemplate.Difficulty;
            share.Created = clock.Now;

            // if block candidate, submit & check if accepted by network
            if(share.IsBlockCandidate)
            {
                logger.Info(() => $"Submitting block {share.BlockHeight} [{share.BlockHash[..6]}]");

                share.IsBlockCandidate = await SubmitBlockAsync(share, blobHex, share.BlockHash);

                if(share.IsBlockCandidate)
                {
                    logger.Info(() => $"Daemon accepted block {share.BlockHeight} [{share.BlockHash[..6]}] submitted by {context.Miner}");

                    OnBlockFound();

                    share.TransactionConfirmationData = share.BlockHash;
                }

                else
                {
                    // clear fields that no longer apply
                    share.TransactionConfirmationData = null;
                }
            }

            return share;
        }

        #endregion // API-Surface

        private static JToken GetFrameAsJToken(byte[] frame)
        {
            var text = Encoding.UTF8.GetString(frame);

            // find end of message type indicator
            var index = text.IndexOf(":");

            if (index == -1)
                return null;

            var json = text.Substring(index + 1);

            return JToken.Parse(json);
        }

        private LibRandomX.randomx_flags? MakeRandomXFlags(JToken token)
        {
            if(token == null)
                return null;

            if(token.Type == JTokenType.Integer)
                return (LibRandomX.randomx_flags) token.Value<ulong>();
            else if(token.Type == JTokenType.String)
            {
                LibRandomX.randomx_flags result = 0;
                var value = token.Value<string>();

                foreach(var flag in value.Split("|").Select(x=> x.Trim()).Where(x=> !string.IsNullOrEmpty(x)))
                {
                    if(Enum.TryParse(typeof(LibRandomX.randomx_flags), flag, true, out var flagVal))
                        result |= (LibRandomX.randomx_flags) flagVal;
                }

                return result;
            }

            return null;
        }

        #region Overrides

        protected override void ConfigureDaemons()
        {
            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            daemon = new DaemonClient(jsonSerializerSettings, messageBus, clusterConfig.ClusterName ?? poolConfig.PoolName, poolConfig.Id);
            daemon.Configure(daemonEndpoints);

            if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
            {
                // also setup wallet daemon
                walletDaemon = new DaemonClient(jsonSerializerSettings, messageBus, clusterConfig.ClusterName ?? poolConfig.PoolName, poolConfig.Id);
                walletDaemon.Configure(walletDaemonEndpoints);
            }
        }

        protected override async Task<bool> AreDaemonsHealthyAsync()
        {
            // test daemons
            var responses = await daemon.ExecuteCmdAllAsync<GetInfoResponse>(logger, CryptonoteCommands.GetInfo);

            if(responses.Where(x => x.Error?.InnerException?.GetType() == typeof(DaemonClientException))
                .Select(x => (DaemonClientException) x.Error.InnerException)
                .Any(x => x.Code == HttpStatusCode.Unauthorized))
                logger.ThrowLogPoolStartupException("Daemon reports invalid credentials");

            if(responses.Any(x => x.Error != null))
                return false;

            if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
            {
                // test wallet daemons
                var responses2 = await walletDaemon.ExecuteCmdAllAsync<object>(logger, CryptonoteWalletCommands.GetAddress);

                if(responses2.Where(x => x.Error?.InnerException?.GetType() == typeof(DaemonClientException))
                    .Select(x => (DaemonClientException) x.Error.InnerException)
                    .Any(x => x.Code == HttpStatusCode.Unauthorized))
                    logger.ThrowLogPoolStartupException("Wallet-Daemon reports invalid credentials");

                return responses2.All(x => x.Error == null);
            }

            return true;
        }

        protected override async Task<bool> AreDaemonsConnectedAsync()
        {
            var response = await daemon.ExecuteCmdAnyAsync<GetInfoResponse>(logger, CryptonoteCommands.GetInfo);

            return response.Error == null && response.Response != null &&
                (response.Response.OutgoingConnectionsCount + response.Response.IncomingConnectionsCount) > 0;
        }

        protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
        {
            var syncPendingNotificationShown = false;

            while(true)
            {
                var request = new GetBlockTemplateRequest
                {
                    WalletAddress = poolConfig.Address,
                    ReserveSize = CryptonoteConstants.ReserveSize
                };

                var responses = await daemon.ExecuteCmdAllAsync<GetBlockTemplateResponse>(logger,
                    CryptonoteCommands.GetBlockTemplate, request);

                var isSynched = responses.All(x => x.Error == null || x.Error.Code != -9);

                if(isSynched)
                {
                    logger.Info(() => "All daemons synched with blockchain");
                    break;
                }

                if(!syncPendingNotificationShown)
                {
                    logger.Info(() => "Daemons still syncing with network. Manager will be started once synced");
                    syncPendingNotificationShown = true;
                }

                await ShowDaemonSyncProgressAsync();

                // delay retry by 5s
                await Task.Delay(5000, ct);
            }
        }

        protected override async Task PostStartInitAsync(CancellationToken ct)
        {
            SetInstanceId();

            // coin config
            var coin = poolConfig.Template.As<CryptonoteCoinTemplate>();
            var infoResponse = await daemon.ExecuteCmdAnyAsync(logger, CryptonoteCommands.GetInfo);

            if(infoResponse.Error != null)
                logger.ThrowLogPoolStartupException($"Init RPC failed: {infoResponse.Error.Message} (Code {infoResponse.Error.Code})");

            if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
            {
                var addressResponse = await walletDaemon.ExecuteCmdAnyAsync<GetAddressResponse>(logger, ct, CryptonoteWalletCommands.GetAddress);

                // ensure pool owns wallet
                if(clusterConfig.PaymentProcessing?.Enabled == true && addressResponse.Response?.Address != poolConfig.Address)
                    logger.ThrowLogPoolStartupException($"Wallet-Daemon does not own pool-address '{poolConfig.Address}'");
            }

            var info = infoResponse.Response.ToObject<GetInfoResponse>();

            // chain detection
            if(!string.IsNullOrEmpty(info.NetType))
            {
                switch(info.NetType.ToLower())
                {
                    case "mainnet":
                        networkType = CryptonoteNetworkType.Main;
                        break;
                    case "stagenet":
                        networkType = CryptonoteNetworkType.Stage;
                        break;
                    case "testnet":
                        networkType = CryptonoteNetworkType.Test;
                        break;
                    default:
                        logger.ThrowLogPoolStartupException($"Unsupport net type '{info.NetType}'");
                        break;
                }
            }

            else
                networkType = info.IsTestnet ? CryptonoteNetworkType.Test : CryptonoteNetworkType.Main;

            // address validation
            poolAddressBase58Prefix = LibCryptonote.DecodeAddress(poolConfig.Address);
            if(poolAddressBase58Prefix == 0)
                logger.ThrowLogPoolStartupException("Unable to decode pool-address");

            switch(networkType)
            {
                case CryptonoteNetworkType.Main:
                    if(poolAddressBase58Prefix != coin.AddressPrefix)
                        logger.ThrowLogPoolStartupException($"Invalid pool address prefix. Expected {coin.AddressPrefix}, got {poolAddressBase58Prefix}");
                    break;

                case CryptonoteNetworkType.Stage:
                    if(poolAddressBase58Prefix != coin.AddressPrefixStagenet)
                        logger.ThrowLogPoolStartupException($"Invalid pool address prefix. Expected {coin.AddressPrefixStagenet}, got {poolAddressBase58Prefix}");
                    break;

                case CryptonoteNetworkType.Test:
                    if(poolAddressBase58Prefix != coin.AddressPrefixTestnet)
                        logger.ThrowLogPoolStartupException($"Invalid pool address prefix. Expected {coin.AddressPrefixTestnet}, got {poolAddressBase58Prefix}");
                    break;
            }

            if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
                ConfigureRewards();

            // update stats
            BlockchainStats.RewardType = "POW";
            BlockchainStats.NetworkType = networkType.ToString();

            await UpdateNetworkStatsAsync();

            // Periodically update network stats
            Observable.Interval(TimeSpan.FromMinutes(1))
                .Select(via => Observable.FromAsync(async () =>
               {
                   try
                   {
                       await UpdateNetworkStatsAsync();
                   }

                   catch(Exception ex)
                   {
                       logger.Error(ex);
                   }
               }))
                .Concat()
                .Subscribe();

            SetupJobUpdates();
        }

        private void SetInstanceId()
        {
            instanceId = new byte[CryptonoteConstants.InstanceIdSize];

            using(var rng = RandomNumberGenerator.Create())
            {
                rng.GetNonZeroBytes(instanceId);
            }

            if(clusterConfig.InstanceId.HasValue)
                instanceId[0] = clusterConfig.InstanceId.Value;
        }

        private void ConfigureRewards()
        {
            // Donation to MiningCore development
            if(networkType == CryptonoteNetworkType.Main &&
                DevDonation.Addresses.TryGetValue(poolConfig.Template.Symbol, out var address))
            {
                poolConfig.RewardRecipients = poolConfig.RewardRecipients.Concat(new[]
                {
                    new RewardRecipient
                    {
                        Address = address,
                        Percentage = DevDonation.Percent,
                        Type = "dev"
                    }
                }).ToArray();
            }
        }

        protected virtual void SetupJobUpdates()
        {
            var blockSubmission = blockFoundSubject.Synchronize();
            var pollTimerRestart = blockFoundSubject.Synchronize();

            var triggers = new List<IObservable<(string Via, string Data)>>
            {
                blockSubmission.Select(x => (JobRefreshBy.BlockFound, (string) null))
            };

            if(extraPoolConfig?.BtStream == null)
            {
                // collect ports
                var zmq = poolConfig.Daemons
                    .Where(x => !string.IsNullOrEmpty(x.Extra.SafeExtensionDataAs<CryptonoteDaemonEndpointConfigExtra>()?.ZmqBlockNotifySocket))
                    .ToDictionary(x => x, x =>
                    {
                        var extra = x.Extra.SafeExtensionDataAs<CryptonoteDaemonEndpointConfigExtra>();
                        var topic = !string.IsNullOrEmpty(extra.ZmqBlockNotifyTopic.Trim()) ? extra.ZmqBlockNotifyTopic.Trim() : BitcoinConstants.ZmqPublisherTopicBlockHash;

                        return (Socket: extra.ZmqBlockNotifySocket, Topic: topic);
                    });

                if(zmq.Count > 0)
                {
                    logger.Info(() => $"Subscribing to ZMQ push-updates from {string.Join(", ", zmq.Values)}");

                    var blockNotify = daemon.ZmqSubscribe(logger, zmq)
                        .Where(msg =>
                        {
                            bool result = false;

                            try
                            {
                                var text = Encoding.UTF8.GetString(msg[0].Read());

                                result = text.StartsWith("json-minimal-chain_main:");
                            }

                            catch
                            {
                            }

                            if(!result)
                                msg.Dispose();

                            return result;
                        })
                        .Select(msg =>
                        {
                            using(msg)
                            {
                                var token = GetFrameAsJToken(msg[0].Read());

                                if (token != null)
                                    return token.Value<long>("first_height").ToString(CultureInfo.InvariantCulture);

                                // We just take the second frame's raw data and turn it into a hex string.
                                // If that string changes, we got an update (DistinctUntilChanged)
                                return msg[0].Read().ToHexString();
                            }
                        })
                        .DistinctUntilChanged()
                        .Select(_ => (JobRefreshBy.PubSub, (string) null))
                        .Publish()
                        .RefCount();

                    pollTimerRestart = Observable.Merge(
                            blockSubmission,
                            blockNotify.Select(_ => Unit.Default))
                        .Publish()
                        .RefCount();

                    triggers.Add(blockNotify);
                }

                if(poolConfig.BlockRefreshInterval > 0)
                {
                    // periodically update block-template
                    var pollingInterval = poolConfig.BlockRefreshInterval > 0 ? poolConfig.BlockRefreshInterval : 1000;

                    triggers.Add(Observable.Timer(TimeSpan.FromMilliseconds(pollingInterval))
                        .TakeUntil(pollTimerRestart)
                        .Select(_ => (JobRefreshBy.Poll, (string) null))
                        .Repeat());
                }

                else
                {
                    // get initial blocktemplate
                    triggers.Add(Observable.Interval(TimeSpan.FromMilliseconds(1000))
                        .Select(_ => (JobRefreshBy.Initial, (string) null))
                        .TakeWhile(_ => !hasInitialBlockTemplate));
                }
            }

            else
            {
                triggers.Add(BtStreamSubscribe(extraPoolConfig.BtStream)
                    .Select(json => (JobRefreshBy.BlockTemplateStream, json))
                    .Publish()
                    .RefCount());

                // get initial blocktemplate
                triggers.Add(Observable.Interval(TimeSpan.FromMilliseconds(1000))
                    .Select(_ => (JobRefreshBy.Initial, (string) null))
                    .TakeWhile(_ => !hasInitialBlockTemplate));
            }

            Blocks = Observable.Merge(triggers)
                .Select(x => Observable.FromAsync(() => UpdateJob(x.Via, x.Data)))
                .Concat()
                .Where(isNew => isNew)
                .Do(_ => hasInitialBlockTemplate = true)
                .Select(_ => Unit.Default)
                .Publish()
                .RefCount();
        }

        #endregion // Overrides
    }
}
