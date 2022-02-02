using System.Globalization;
using System.Numerics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using Autofac;
using Miningcore.Blockchain.Bitcoin;
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
using Newtonsoft.Json;
using NLog;
using Block = Miningcore.Blockchain.Ethereum.DaemonResponses.Block;
using Contract = Miningcore.Contracts.Contract;
using EC = Miningcore.Blockchain.Ethereum.EthCommands;
using static Miningcore.Util.ActionUtils;
using System.Reactive;
using Miningcore.Mining;
using Miningcore.Rpc;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Ethereum;

public class EthereumJobManager : JobManagerBase<EthereumJob>
{
    public EthereumJobManager(
        IComponentContext ctx,
        IMasterClock clock,
        IMessageBus messageBus,
        IExtraNonceProvider extraNonceProvider) :
        base(ctx, messageBus)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(messageBus);
        Contract.RequiresNonNull(extraNonceProvider);

        this.clock = clock;
        this.extraNonceProvider = extraNonceProvider;
    }

    private DaemonEndpointConfig[] daemonEndpoints;
    private RpcClient rpc;
    private EthereumNetworkType networkType;
    private GethChainType chainType;
    private EthashFull ethash;
    private readonly IMasterClock clock;
    private readonly IExtraNonceProvider extraNonceProvider;
    private const int MaxBlockBacklog = 6;
    protected readonly Dictionary<string, EthereumJob> validJobs = new();
    private EthereumPoolConfigExtra extraPoolConfig;

    protected async Task<bool> UpdateJob(CancellationToken ct, string via = null)
    {
        try
        {
            var bt = await GetBlockTemplateAsync(ct);

            if(bt == null)
                return false;

            return UpdateJob(bt, via);
        }

        catch(OperationCanceledException)
        {
            // ignored
        }

        catch(Exception ex)
        {
            logger.Error(ex, () => $"Error during {nameof(UpdateJob)}");
        }

        return false;
    }

    protected bool UpdateJob(EthereumBlockTemplate blockTemplate, string via = null)
    {
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

                logger.Info(() => $"New work at height {currentJob.BlockTemplate.Height} and header {currentJob.BlockTemplate.Header} via [{(via ?? "Unknown")}]");

                // update stats
                BlockchainStats.LastNetworkBlockTime = clock.Now;
                BlockchainStats.BlockHeight = job.BlockTemplate.Height;
                BlockchainStats.NetworkDifficulty = job.BlockTemplate.Difficulty;
                BlockchainStats.NextNetworkTarget = job.BlockTemplate.Target;
                BlockchainStats.NextNetworkBits = "";
            }

            return isNew;
        }

        catch(OperationCanceledException)
        {
            // ignored
        }

        catch(Exception ex)
        {
            logger.Error(ex, () => $"Error during {nameof(UpdateJob)}");
        }

        return false;
    }

    private async Task<EthereumBlockTemplate> GetBlockTemplateAsync(CancellationToken ct)
    {
        var requests = new[]
        {
            new RpcRequest(EC.GetWork),
            new RpcRequest(EC.GetBlockByNumber, new[] { (object) "latest", true })
        };

        var responses = await rpc.ExecuteBatchAsync(logger, ct, requests);

        if(responses.Any(x => x.Error != null))
        {
            logger.Warn(() => $"Error(s) refreshing blocktemplate: {responses.First(x => x.Error != null).Error.Message}");
            return null;
        }

        // extract results
        var work = responses[0].Response.ToObject<string[]>();
        var block = responses[1].Response.ToObject<Block>();

        if(work == null)
            return null;

        // append blockheight (Recent versions of geth return this as the 4th element in the getWork response, older geth does not)
        if(work.Length < 4)
        {
            if(block == null)
                return null;

            var currentHeight = block.Height!.Value;
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
        var syncStateResponse = await rpc.ExecuteAsync<object>(logger, EC.GetSyncState, ct);

        if(syncStateResponse.Error == null)
        {
            // eth_syncing returns false if not synching
            if(syncStateResponse.Response is false)
                return;

            if(syncStateResponse.Response is JObject obj)
            {
                var syncState = obj.ToObject<SyncState>();

                // get peer count
                var getPeerCountResponse = await rpc.ExecuteAsync<string>(logger, EC.GetPeerCount, ct);
                var peerCount = getPeerCountResponse.Response.IntegralFromHex<uint>();

                if(syncState?.WarpChunksAmount.HasValue == true)
                {
                    var warpChunkAmount = syncState.WarpChunksAmount.Value;
                    var warpChunkProcessed = syncState.WarpChunksProcessed.Value;
                    var percent = (double) warpChunkProcessed / warpChunkAmount * 100;

                    logger.Info(() => $"Daemon has downloaded {percent:0.00}% of warp-chunks from {peerCount} peers");
                }

                else if(syncState?.HighestBlock.HasValue == true && syncState.CurrentBlock.HasValue)
                {
                    var lowestHeight = syncState.CurrentBlock.Value;
                    var totalBlocks = syncState.HighestBlock.Value;
                    var blocksPercent = (double) lowestHeight / totalBlocks * 100;

                    if(syncState.KnownStates.HasValue)
                    {
                        var knownStates = syncState.KnownStates.Value;
                        var pulledStates = syncState.PulledStates.Value;
                        var statesPercent = (double) pulledStates / knownStates * 100;

                        logger.Info(() => $"Daemon has downloaded {blocksPercent:0.00}% of blocks and {statesPercent:0.00}% of states from {peerCount} peers");
                    }

                    else
                        logger.Info(() => $"Daemon has downloaded {blocksPercent:0.00}% of blocks from {peerCount} peers");
                }
            }
        }
    }

    private async Task UpdateNetworkStatsAsync(CancellationToken ct)
    {
        try
        {
            var requests = new[]
            {
                new RpcRequest(EC.GetPeerCount),
                new RpcRequest(EC.GetBlockByNumber, new[] { (object) "latest", true })
            };

            var responses = await rpc.ExecuteBatchAsync(logger, ct, requests);

            if(responses.Any(x => x.Error != null))
            {
                var errors = responses.Where(x => x.Error != null)
                    .ToArray();

                if(errors.Any())
                    logger.Warn(() => $"Error(s) refreshing network stats: {string.Join(", ", errors.Select(y => y.Error.Message))})");
            }

            // extract results
            var peerCount = responses[0].Response.ToObject<string>().IntegralFromHex<int>();
            var blockInfo = responses[1].Response.ToObject<Block>();

            var latestBlockHeight = blockInfo!.Height.Value;
            var latestBlockTimestamp = blockInfo.Timestamp;
            var latestBlockDifficulty = blockInfo.Difficulty.IntegralFromHex<ulong>();

            var sampleSize = (ulong) 300;
            var sampleBlockNumber = latestBlockHeight - sampleSize;
            var sampleBlockResults = await rpc.ExecuteAsync<Block>(logger, EC.GetBlockByNumber, ct, new[] { (object) sampleBlockNumber.ToStringHexWithPrefix(), true });
            var sampleBlockTimestamp = sampleBlockResults.Response.Timestamp;

            var blockTime = (double) (latestBlockTimestamp - sampleBlockTimestamp) / sampleSize;
            var networkHashrate = latestBlockDifficulty / blockTime;

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
        var response = await rpc.ExecuteAsync<object>(logger, EC.SubmitWork, CancellationToken.None, new[]
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

    public object[] GetJobParamsForStratum()
    {
        var job = currentJob;

        return job?.GetJobParamsForStratum() ?? Array.Empty<object>();
    }

    public object[] GetWorkParamsForStratum(EthereumWorkerContext context)
    {
        var job = currentJob;

        return job?.GetWorkParamsForStratum(context) ?? Array.Empty<object>();
    }

    #region API-Surface

    public IObservable<Unit> Jobs { get; private set; }

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        extraPoolConfig = pc.Extra.SafeExtensionDataAs<EthereumPoolConfigExtra>();

        // extract standard daemon endpoints
        daemonEndpoints = pc.Daemons
            .Where(x => string.IsNullOrEmpty(x.Category))
            .ToArray();

        base.Configure(pc, cc);

        if(pc.EnableInternalStratum == true)
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

    public async Task<Share> SubmitShareV1Async(StratumConnection worker, string[] request, string workerName, CancellationToken ct)
    {
        Contract.RequiresNonNull(worker);
        Contract.RequiresNonNull(request);

        var context = worker.ContextAs<EthereumWorkerContext>();
        var nonce = request[0];
        var header = request[1];

        EthereumJob job;

        // stale?
        lock(jobLock)
        {
            job = validJobs.Values.FirstOrDefault(x => x.BlockTemplate.Header.Equals(header));

            if(job == null)
                throw new StratumException(StratumError.MinusOne, "stale share");
        }

        return await SubmitShareAsync(worker, context, workerName, job, nonce.StripHexPrefix(), ct);
    }

    public async Task<Share> SubmitShareV2Async(StratumConnection worker, string[] request, CancellationToken ct)
    {
        Contract.RequiresNonNull(worker);
        Contract.RequiresNonNull(request);

        var context = worker.ContextAs<EthereumWorkerContext>();
        var jobId = request[1];
        var nonce = request[2];

        EthereumJob job;

        // stale?
        lock(jobLock)
        {
            // look up job by id
            if(!validJobs.TryGetValue(jobId, out job))
                throw new StratumException(StratumError.MinusOne, "stale share");
        }

        // assemble full-nonce
        var fullNonceHex = context.ExtraNonce1 + nonce;

        return await SubmitShareAsync(worker, context, context.Worker, job, fullNonceHex, ct);
    }

    private async Task<Share> SubmitShareAsync(StratumConnection worker,
        EthereumWorkerContext context, string workerName, EthereumJob job, string nonce, CancellationToken ct)
    {
        // validate & process
        var (share, fullNonceHex, headerHash, mixHash) = await job.ProcessShareAsync(worker, workerName, nonce, ethash, ct);

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

                OnBlockFound();
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

        rpc = new RpcClient(daemonEndpoints.First(), jsonSerializerSettings, messageBus, poolConfig.Id);
    }

    protected override async Task<bool> AreDaemonsHealthyAsync(CancellationToken ct)
    {
        var response = await rpc.ExecuteAsync<Block>(logger, EC.GetBlockByNumber, ct, new[] { (object) "latest", true });

        return response.Error == null;
    }

    protected override async Task<bool> AreDaemonsConnectedAsync(CancellationToken ct)
    {
        var response = await rpc.ExecuteAsync<string>(logger, EC.GetPeerCount, ct);

        return response.Error == null && response.Response.IntegralFromHex<uint>() > 0;
    }

    protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        var syncPendingNotificationShown = false;

        do
        {
            var syncStateResponse = await rpc.ExecuteAsync<object>(logger, EC.GetSyncState, ct);

            var isSynched = syncStateResponse.Response is false;

            if(isSynched)
            {
                logger.Info(() => "All daemons synched with blockchain");
                break;
            }

            if(!syncPendingNotificationShown)
            {
                logger.Info(() => "Daemon is still syncing with network. Manager will be started once synced.");
                syncPendingNotificationShown = true;
            }

            await ShowDaemonSyncProgressAsync(ct);
        } while(await timer.WaitForNextTickAsync(ct));
    }

    protected override async Task PostStartInitAsync(CancellationToken ct)
    {
        var requests = new[]
        {
            new RpcRequest(EC.GetNetVersion),
            new RpcRequest(EC.GetAccounts),
            new RpcRequest(EC.GetCoinbase),
        };

        var responses = await rpc.ExecuteBatchAsync(logger, ct, requests);

        if(responses.Any(x => x.Error != null))
        {
            var errors = responses.Take(3).Where(x => x.Error != null)
                .ToArray();

            if(errors.Any())
                throw new PoolStartupException($"Init RPC failed: {string.Join(", ", errors.Select(y => y.Error.Message))}", poolConfig.Id);
        }

        // extract results
        var netVersion = responses[0].Response.ToObject<string>();
        // var accounts = responses[1].Response.ToObject<string[]>();
        // var coinbase = responses[2].Response.ToObject<string>();
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
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            do
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
            } while(await timer.WaitForNextTickAsync(ct));
        }

        await SetupJobUpdates(ct);
    }

    protected virtual async Task SetupJobUpdates(CancellationToken ct)
    {
        var pollingInterval = poolConfig.BlockRefreshInterval > 0 ? poolConfig.BlockRefreshInterval : 1000;

        var blockSubmission = blockFoundSubject.Synchronize();
        var pollTimerRestart = blockFoundSubject.Synchronize();

        var triggers = new List<IObservable<(string Via, string Data)>>
        {
            blockSubmission.Select(_ => (JobRefreshBy.BlockFound, (string) null))
        };

        var endpointExtra = daemonEndpoints
            .Where(x => x.Extra.SafeExtensionDataAs<EthereumDaemonEndpointConfigExtra>() != null)
            .Select(x=> Tuple.Create(x, x.Extra.SafeExtensionDataAs<EthereumDaemonEndpointConfigExtra>()))
            .FirstOrDefault();

        if(endpointExtra?.Item2?.PortWs.HasValue == true)
        {
            var (endpointConfig, extra) = endpointExtra;

            var wsEndpointConfig = new DaemonEndpointConfig
            {
                Host = endpointConfig.Host,
                Port = extra.PortWs!.Value,
                HttpPath = extra.HttpPathWs,
                Ssl = extra.SslWs
            };

            logger.Info(() => $"Subscribing to WebSocket {(wsEndpointConfig.Ssl ? "wss" : "ws")}://{wsEndpointConfig.Host}:{wsEndpointConfig.Port}");

            var wsSubscription = "newHeads";
            var isRetry = false;

        retry:
            // stream work updates
            var getWorkObs = rpc.WebsocketSubscribe(logger, ct, wsEndpointConfig, EC.Subscribe, new[] { wsSubscription })
                .Publish()
                .RefCount();

            // test subscription
            var subcriptionResponse = await getWorkObs
                .Take(1)
                .Select(x => JsonConvert.DeserializeObject<JsonRpcResponse<string>>(Encoding.UTF8.GetString(x)))
                .ToTask(ct);

            if(subcriptionResponse.Error != null)
            {
                // older versions of geth only support subscriptions to "newBlocks"
                if(!isRetry && subcriptionResponse.Error.Code == (int) BitcoinRPCErrorCode.RPC_METHOD_NOT_FOUND)
                {
                    wsSubscription = "newBlocks";

                    isRetry = true;
                    goto retry;
                }

                throw new PoolStartupException($"Unable to subscribe to geth websocket '{wsSubscription}': {subcriptionResponse.Error.Message} [{subcriptionResponse.Error.Code}]", poolConfig.Id);
            }

            var websocketNotify = getWorkObs.Where(x => x != null)
                .Publish()
                .RefCount();

            pollTimerRestart = blockSubmission.Merge(websocketNotify.Select(_ => Unit.Default))
                .Publish()
                .RefCount();

            triggers.Add(websocketNotify.Select(_ => (JobRefreshBy.WebSocket, (string) null)));

            triggers.Add(Observable.Timer(TimeSpan.FromMilliseconds(pollingInterval))
                .TakeUntil(pollTimerRestart)
                .Select(_ => (JobRefreshBy.Poll, (string) null))
                .Repeat());
        }

        else
        {
            // ordinary polling (avoid this at all cost)
            triggers.Add(Observable.Timer(TimeSpan.FromMilliseconds(pollingInterval))
                .TakeUntil(pollTimerRestart)
                .Select(_ => (JobRefreshBy.Poll, (string) null))
                .Repeat());
        }

        Jobs = triggers.Merge()
            .Select(x => Observable.FromAsync(() => UpdateJob(ct, x.Via)))
            .Concat()
            .Where(isNew => isNew)
            .Select(_ => Unit.Default)
            .Publish()
            .RefCount();
    }

    #endregion // Overrides
}
