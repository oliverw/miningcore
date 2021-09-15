using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Miningcore.Blockchain.Ethereum.Configuration;
using Miningcore.Blockchain.Ethereum.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Crypto.Hashing.Ethash;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using NLog;
using Block = Miningcore.Blockchain.Ethereum.DaemonResponses.Block;
using Contract = Miningcore.Contracts.Contract;
using EC = Miningcore.Blockchain.Ethereum.EthCommands;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Ethereum
{
    public class EthereumJobManager : JobManagerBase<EthereumJob>
    {
        public EthereumJobManager(
            IComponentContext ctx,
            IMasterClock clock,
            IMessageBus messageBus,
            IExtraNonceProvider extraNonceProvider,
            JsonSerializerSettings serializerSettings) :
            base(ctx, messageBus)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));
            Contract.RequiresNonNull(extraNonceProvider, nameof(extraNonceProvider));

            this.clock = clock;
            this.extraNonceProvider = extraNonceProvider;

            serializer = new JsonSerializer
            {
                ContractResolver = serializerSettings.ContractResolver
            };
        }

        private DaemonEndpointConfig[] daemonEndpoints;
        private RpcClient rpcClient;
        private EthereumNetworkType networkType;
        private GethChainType chainType;
        private EthashFull ethash;
        private readonly IMasterClock clock;
        private readonly IExtraNonceProvider extraNonceProvider;

        private const int MaxBlockBacklog = 3;
        protected readonly Dictionary<string, EthereumJob> validJobs = new();
        private EthereumPoolConfigExtra extraPoolConfig;
        private readonly JsonSerializer serializer;

        protected async Task<bool> UpdateJobAsync(CancellationToken ct)
        {
            logger.LogInvoke();

            try
            {
                return UpdateJob(await GetBlockTemplateAsync(ct));
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"Error during {nameof(UpdateJobAsync)}");
            }

            return false;
        }

        protected bool UpdateJob(EthereumBlockTemplate blockTemplate)
        {
            logger.LogInvoke();

            try
            {
                // may happen if daemon is currently not connected to peers
                if(blockTemplate == null || blockTemplate.Header?.Length == 0)
                    return false;

                var job = currentJob;
                var isNew = currentJob == null ||
                    job.BlockTemplate.Height < blockTemplate.Height ||
                    job.BlockTemplate.Header != blockTemplate.Header;

                if(isNew)
                {
                    messageBus.NotifyChainHeight(poolConfig.Id, blockTemplate.Height, poolConfig.Template);

                    var jobId = NextJobId("x8");

                    // update template
                    job = new EthereumJob(jobId, blockTemplate, logger);

                    lock(jobLock)
                    {
                        // add jobs
                        validJobs[jobId] = job;

                        // remove old ones
                        var obsoleteKeys = validJobs.Keys
                            .Where(key => validJobs[key].BlockTemplate.Height < job.BlockTemplate.Height - MaxBlockBacklog).ToArray();

                        foreach(var key in obsoleteKeys)
                            validJobs.Remove(key);
                    }

                    currentJob = job;

                    // update stats
                    BlockchainStats.LastNetworkBlockTime = clock.Now;
                    BlockchainStats.BlockHeight = job.BlockTemplate.Height;
                    BlockchainStats.NetworkDifficulty = job.BlockTemplate.Difficulty;
                    BlockchainStats.NextNetworkTarget = job.BlockTemplate.Target;
                    BlockchainStats.NextNetworkBits = "";
                }

                return isNew;
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"Error during {nameof(UpdateJob)}");
            }

            return false;
        }

        private async Task<EthereumBlockTemplate> GetBlockTemplateAsync(CancellationToken ct)
        {
            logger.LogInvoke();

            var requests = new[]
            {
                new RpcRequest(EC.GetWork),
                new RpcRequest(EC.GetBlockByNumber, new[] { (object) "latest", true })
            };

            var responses = await rpcClient.ExecuteBatchAsync(logger, ct, requests);

            if(responses.Any(x => x.Error != null))
            {
                logger.Warn(() => $"Error(s) refreshing blocktemplate: {responses.First(x => x.Error != null).Error.Message}");
                return null;
            }

            // extract results
            var work = responses[0].Response.ToObject<string[]>();
            var block = responses[1].Response.ToObject<Block>();

            // append blockheight (Recent versions of geth return this as the 4th element in the getWork response, older geth does not)
            if(work.Length < 4)
            {
                var currentHeight = block.Height.Value;
                work = work.Concat(new[] { (currentHeight + 1).ToStringHexWithPrefix() }).ToArray();
            }

            // extract values
            var height = work[3].IntegralFromHex<ulong>();
            var targetString = work[2];
            var target = BigInteger.Parse(targetString.Substring(2), NumberStyles.HexNumber);

            var result = new EthereumBlockTemplate
            {
                Header = work[0],
                Seed = work[1],
                Target = targetString,
                Difficulty = (ulong) BigInteger.Divide(EthereumConstants.BigMaxValue, target),
                Height = height
            };

            return result;
        }

        private async Task ShowDaemonSyncProgressAsync(CancellationToken ct)
        {
            var syncStateResponse = await rpcClient.ExecuteAsync<object>(logger, EC.GetSyncState, ct);

            if(syncStateResponse.Error == null)
            {
                // eth_syncing returns false if not synching
                if(syncStateResponse.Response is false)
                    return;

                if(syncStateResponse.Response is SyncState syncState)
                {
                    // get peer count
                    var getPeerCountResponse = await rpcClient.ExecuteAsync<string>(logger, EC.GetPeerCount, ct);
                    var peerCount = getPeerCountResponse.Response.IntegralFromHex<uint>();

                    if(syncState.WarpChunksAmount != 0)
                    {
                        var warpChunkAmount = syncState.WarpChunksAmount;
                        var warpChunkProcessed = syncState.WarpChunksProcessed;
                        var percent = (double) warpChunkProcessed / warpChunkAmount * 100;

                        logger.Info(() => $"Daemons have downloaded {percent:0.00}% of warp-chunks from {peerCount} peers");
                    }

                    else if(syncState.HighestBlock != 0)
                    {
                        var lowestHeight = syncState.CurrentBlock;
                        var totalBlocks = syncState.HighestBlock;
                        var percent = (double) lowestHeight / totalBlocks * 100;

                        logger.Info(() => $"Daemons have downloaded {percent:0.00}% of blockchain from {peerCount} peers");
                    }
                }
            }
        }

        private async Task UpdateNetworkStatsAsync(CancellationToken ct)
        {
            logger.LogInvoke();

            try
            {
                var requests = new[]
                {
                    new RpcRequest(EC.GetPeerCount),
                    new RpcRequest(EC.GetBlockByNumber, new[] { (object) "latest", true })
                };

                var responses = await rpcClient.ExecuteBatchAsync(logger, ct, requests);

                if(responses.Any(x => x.Error != null))
                {
                    var errors = responses.Where(x => x.Error != null)
                        .ToArray();

                    if(errors.Any())
                        logger.Warn(() => $"Error(s) refreshing network stats: {string.Join(", ", errors.Select(y => y.Error.Message))})");
                }

                // extract results
                var peerCount = responses[0].Response.ToObject<string>().IntegralFromHex<int>();
                var latestBlockInfo = responses[1].Response.ToObject<Block>();

                var latestBlockHeight = latestBlockInfo.Height.Value;
                var latestBlockTimestamp = latestBlockInfo.Timestamp;
                var latestBlockDifficulty = latestBlockInfo.Difficulty.IntegralFromHex<ulong>();

                var sampleSize = (ulong) 300;
                var sampleBlockNumber = latestBlockHeight - sampleSize;
                var sampleBlockResults = await rpcClient.ExecuteAsync<Block>(logger, EC.GetBlockByNumber, ct, new[] { (object) sampleBlockNumber.ToStringHexWithPrefix(), true });
                var sampleBlockTimestamp = sampleBlockResults.Response.Timestamp;

                var blockTime = (double) (latestBlockTimestamp - sampleBlockTimestamp) / sampleSize;
                var networkHashrate = (double) (latestBlockDifficulty / blockTime);

                BlockchainStats.NetworkHashrate = blockTime > 0 ? networkHashrate : 0;
                BlockchainStats.ConnectedPeers = peerCount;
            }

            catch(Exception e)
            {
                logger.Error(e);
            }
        }

        private async Task<bool> SubmitBlockAsync(Share share, string fullNonceHex, string headerHash, string mixHash)
        {
            // submit work
            var response = await rpcClient.ExecuteAsync<object>(logger, EC.SubmitWork, CancellationToken.None, new[]
            {
                fullNonceHex,
                headerHash,
                mixHash
            });

            if(response.Error != null || (bool?) response.Response == false)
            {
                var error = response.Error?.Message ?? response?.Response?.ToString();

                logger.Warn(() => $"Block {share.BlockHeight} submission failed with: {error}");
                messageBus.SendMessage(new AdminNotification("Block submission failed", $"Pool {poolConfig.Id} {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}failed to submit block {share.BlockHeight}: {error}"));

                return false;
            }

            return true;
        }

        private object[] GetJobParamsForStratum(bool isNew)
        {
            var job = currentJob;

            if(job != null)
            {
                return new object[]
                {
                    job.Id,
                    job.BlockTemplate.Seed.StripHexPrefix(),
                    job.BlockTemplate.Header.StripHexPrefix(),
                    isNew
                };
            }

            return new object[0];
        }

        #region API-Surface

        public IObservable<object> Jobs { get; private set; }

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<EthereumPoolConfigExtra>();

            // extract standard daemon endpoints
            daemonEndpoints = poolConfig.Daemons
                .Where(x => string.IsNullOrEmpty(x.Category))
                .ToArray();

            base.Configure(poolConfig, clusterConfig);

            if(poolConfig.EnableInternalStratum == true)
            {
                // ensure dag location is configured
                var dagDir = !string.IsNullOrEmpty(extraPoolConfig?.DagDir) ?
                    Environment.ExpandEnvironmentVariables(extraPoolConfig.DagDir) :
                    Dag.GetDefaultDagDirectory();

                // create it if necessary
                Directory.CreateDirectory(dagDir);

                // setup ethash
                ethash = new EthashFull(3, dagDir);
            }
        }

        public bool ValidateAddress(string address)
        {
            if(string.IsNullOrEmpty(address))
                return false;

            if(EthereumConstants.ZeroHashPattern.IsMatch(address) ||
                !EthereumConstants.ValidAddressPattern.IsMatch(address))
                return false;

            return true;
        }

        public void PrepareWorker(StratumConnection client)
        {
            var context = client.ContextAs<EthereumWorkerContext>();
            context.ExtraNonce1 = extraNonceProvider.Next();
        }

        public async ValueTask<Share> SubmitShareAsync(StratumConnection worker, string[] request, CancellationToken ct)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.RequiresNonNull(request, nameof(request));

            logger.LogInvoke(new object[] { worker.ConnectionId });
            var context = worker.ContextAs<EthereumWorkerContext>();

            // var miner = request[0];
            var jobId = request[1];
            var nonce = request[2];
            EthereumJob job;

            // stale?
            lock(jobLock)
            {
                if(!validJobs.TryGetValue(jobId, out job))
                    throw new StratumException(StratumError.MinusOne, "stale share");
            }

            // validate & process
            var (share, fullNonceHex, headerHash, mixHash) = await job.ProcessShareAsync(worker, nonce, ethash, ct);

            // enrich share with common data
            share.PoolId = poolConfig.Id;
            share.NetworkDifficulty = BlockchainStats.NetworkDifficulty;
            share.Source = clusterConfig.ClusterName;
            share.Created = clock.Now;

            // if block candidate, submit & check if accepted by network
            if(share.IsBlockCandidate)
            {
                logger.Info(() => $"Submitting block {share.BlockHeight}");

                share.IsBlockCandidate = await SubmitBlockAsync(share, fullNonceHex, headerHash, mixHash);

                if(share.IsBlockCandidate)
                {
                    logger.Info(() => $"Daemon accepted block {share.BlockHeight} submitted by {context.Miner}");
                }
            }

            return share;
        }

        public BlockchainStats BlockchainStats { get; } = new();

        #endregion // API-Surface

        #region Overrides

        protected override void ConfigureDaemons()
        {
            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            rpcClient = new RpcClient(daemonEndpoints.First(), jsonSerializerSettings, messageBus, poolConfig.Id);
        }

        protected override async Task<bool> AreDaemonsHealthyAsync(CancellationToken ct)
        {
            var response = await rpcClient.ExecuteAsync<Block>(logger, EC.GetBlockByNumber, ct, new[] { (object) "latest", true });

            return response.Error == null;
        }

        protected override async Task<bool> AreDaemonsConnectedAsync(CancellationToken ct)
        {
            var response = await rpcClient.ExecuteAsync<string>(logger, EC.GetPeerCount, ct);

            return response.Error == null && response.Response.IntegralFromHex<uint>() > 0;
        }

        protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
        {
            var syncPendingNotificationShown = false;

            while(true)
            {
                var responses = await rpcClient.ExecuteAsync<object>(logger, EC.GetSyncState, ct);

                var isSynched = responses.Response is false;

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

                await ShowDaemonSyncProgressAsync(ct);

                // delay retry by 5s
                await Task.Delay(5000, ct);
            }
        }

        protected override async Task PostStartInitAsync(CancellationToken ct)
        {
            var requests = new[]
            {
                new RpcRequest(EC.GetNetVersion),
                new RpcRequest(EC.GetAccounts),
                new RpcRequest(EC.GetCoinbase),
            };

            var responses = await rpcClient.ExecuteBatchAsync(logger, ct, requests);

            if(responses.Any(x => x.Error != null))
            {
                var errors = responses.Take(3).Where(x => x.Error != null)
                    .ToArray();

                if(errors.Any())
                    logger.ThrowLogPoolStartupException($"Init RPC failed: {string.Join(", ", errors.Select(y => y.Error.Message))}");
            }

            // extract results
            var netVersion = responses[0].Response.ToObject<string>();
            var accounts = responses[1].Response.ToObject<string[]>();
            var coinbase = responses[2].Response.ToObject<string>();
            var gethChain = extraPoolConfig?.ChainTypeOverride ?? "Ethereum";

            EthereumUtils.DetectNetworkAndChain(netVersion, gethChain, out networkType, out chainType);

            // update stats
            BlockchainStats.RewardType = "POW";
            BlockchainStats.NetworkType = $"{chainType}-{networkType}";

            await UpdateNetworkStatsAsync(ct);

            // Periodically update network stats
            Observable.Interval(TimeSpan.FromMinutes(10))
                .Select(via => Observable.FromAsync(() =>
                    Guard(()=> UpdateNetworkStatsAsync(ct),
                        ex=> logger.Error(ex))))
                .Concat()
                .Subscribe();

            if(poolConfig.EnableInternalStratum == true)
            {
                // make sure we have a current DAG
                while(true)
                {
                    var blockTemplate = await GetBlockTemplateAsync(ct);

                    if(blockTemplate != null)
                    {
                        logger.Info(() => "Loading current DAG ...");

                        await ethash.GetDagAsync(blockTemplate.Height, logger, ct);

                        logger.Info(() => "Loaded current DAG");
                        break;
                    }

                    logger.Info(() => "Waiting for first valid block template");
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
            }

            ConfigureRewards();

            SetupJobUpdates(ct);
        }

        private void ConfigureRewards()
        {
            // Donation to MiningCore development
            if(networkType == EthereumNetworkType.Mainnet &&
                chainType == GethChainType.Ethereum &&
                DevDonation.Addresses.TryGetValue(poolConfig.Template.As<CoinTemplate>().Symbol, out var address))
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

        protected virtual void SetupJobUpdates(CancellationToken cancellationToken)
        {
            var pollingInterval = poolConfig.BlockRefreshInterval > 0 ? poolConfig.BlockRefreshInterval : 1000;

            Jobs = Observable.Interval(TimeSpan.FromMilliseconds(pollingInterval))
                .Select(_ => Observable.FromAsync(UpdateJobAsync))
                .Concat()
                .Do(isNew =>
                {
                    if(isNew)
                        logger.Info(() => $"New work at height {currentJob.BlockTemplate.Height} and header {currentJob.BlockTemplate.Header} detected [{JobRefreshBy.Poll}]");
                })
                .Where(isNew => isNew)
                .Select(_ => GetJobParamsForStratum(true))
                .Publish()
                .RefCount();
        }

        #endregion // Overrides
    }
}
