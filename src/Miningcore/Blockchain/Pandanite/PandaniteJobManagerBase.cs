using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using Autofac;
using Miningcore.Blockchain.Pandanite.Configuration;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Rpc;
using Miningcore.Time;
using NBitcoin;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Pandanite;

public abstract class PandaniteJobManagerBase<TJob> : JobManagerBase<TJob>
{
    protected IPandaniteNodeApi Node;

    protected PandaniteJobManagerBase(
        IComponentContext ctx,
        IMasterClock clock,
        IMessageBus messageBus) :
        base(ctx, messageBus)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(messageBus);

        this.clock = clock;
    }

    protected readonly IMasterClock clock;
    protected RpcClient rpc;
    protected readonly IExtraNonceProvider extraNonceProvider;
    protected const int ExtranonceBytes = 4;
    protected int maxActiveJobs = 4;
    protected bool hasLegacyDaemon;
    protected PandanitePoolConfigExtra extraPoolConfig;
    protected PandanitePoolPaymentProcessingConfigExtra extraPoolPaymentProcessingConfig;
    protected readonly List<TJob> validJobs = new();
    protected DateTime? lastJobRebroadcast;
    protected bool hasSubmitBlockMethod;
    protected bool isPoS;
    protected TimeSpan jobRebroadcastTimeout;
    protected Network network;
    protected IDestination poolAddressDestination;

    protected virtual void SetupJobUpdates(CancellationToken ct)
    {
        jobRebroadcastTimeout = TimeSpan.FromSeconds(Math.Max(1, poolConfig.JobRebroadcastTimeout));
        var blockFound = blockFoundSubject.Synchronize();
        var pollTimerRestart = blockFoundSubject.Synchronize();

        var triggers = new List<IObservable<(bool Force, string Via, string Data)>>
        {
            blockFound.Select(_ => (false, JobRefreshBy.BlockFound, (string) null))
        };

        // periodically update transactions for current template
        if(poolConfig.JobRebroadcastTimeout > 0)
        {
            triggers.Add(Observable.Timer(jobRebroadcastTimeout)
                .TakeUntil(pollTimerRestart)
                .Select(_ => (true, JobRefreshBy.PollRefresh, (string) null))
                .Repeat());
        }

        // get initial blocktemplate
        triggers.Add(Observable.Interval(TimeSpan.FromMilliseconds(1000))
            .Select(_ => (true, JobRefreshBy.Initial, (string) null))
            .TakeWhile(_ => !hasInitialBlockTemplate));

        Jobs = triggers.Merge()
            .Select(x => Observable.FromAsync(() => UpdateJob(ct, x.Force, x.Via, x.Data)))
            .Concat()
            .Where(x => x.IsNew || x.Force)
            .Do(x =>
            {
                if(x.IsNew)
                    hasInitialBlockTemplate = true;
            })
            .Select(x => GetJobParamsForStratum(x.IsNew))
            .Publish()
            .RefCount();
    }

    protected virtual async Task ShowDaemonSyncProgressAsync(CancellationToken ct)
    {
        /*if(hasLegacyDaemon)
        {
            await ShowDaemonSyncProgressLegacyAsync(ct);
            return;
        }

        var info = await rpc.ExecuteAsync<BlockchainInfo>(logger, BitcoinCommands.GetBlockchainInfo, ct);

        if(info != null)
        {
            var blockCount = info.Response?.Blocks;

            if(blockCount.HasValue)
            {
                // get list of peers and their highest block height to compare to ours
                var peerInfo = await rpc.ExecuteAsync<PeerInfo[]>(logger, BitcoinCommands.GetPeerInfo, ct);
                var peers = peerInfo.Response;

                var totalBlocks = Math.Max(info.Response.Headers, peers.Any() ? peers.Max(y => y.StartingHeight) : 0);

                var percent = totalBlocks > 0 ? (double) blockCount / totalBlocks * 100 : 0;
                logger.Info(() => $"Daemon has downloaded {percent:0.00}% of blockchain from {peers.Length} peers");
            }
        }*/
        await Task.CompletedTask;
    }

    private async Task UpdateNetworkStatsAsync(CancellationToken ct)
    {
        var ( _, hashrate) = await Node.GetNetworkHashrate();
        var ( _, peers) = await Node.GetPeers();

        BlockchainStats.NetworkHashrate = hashrate;
        BlockchainStats.ConnectedPeers = Math.Max(0, peers.Count - 1);

        await Task.CompletedTask;
    }

    protected record SubmitResult(bool Accepted, string CoinbaseTx);

    protected async Task<SubmitResult> SubmitBlockAsync(IPandaniteNodeApi api, Share share, byte[] block, CancellationToken ct)
    {
        using var stream = new MemoryStream(block);

        var result = await api.Submit(stream);

        // TODO: Get error from api
        if(!result)
        {
            logger.Warn(() => $"Block {share.BlockHeight} submission failed with: {result}");
            messageBus.SendMessage(new AdminNotification("Block submission failed", $"Pool {poolConfig.Id} {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}failed to submit block {share.BlockHeight}: {result}"));
            return new SubmitResult(false, null);
        }

        // TODO: verify share.TransactionConfirmationData



        /*
        // was it accepted?
        var acceptResult = results[1];
        var block = acceptResult.Response?.ToObject<DaemonResponses.Block>();
        var accepted = acceptResult.Error == null && block?.Hash == share.BlockHash;

        if(!accepted)
        {
            logger.Warn(() => $"Block {share.BlockHeight} submission failed for pool {poolConfig.Id} because block was not found after submission");
            messageBus.SendMessage(new AdminNotification($"[{share.PoolId.ToUpper()}]-[{share.Source}] Block submission failed", $"[{share.PoolId.ToUpper()}]-[{share.Source}] Block {share.BlockHeight} submission failed for pool {poolConfig.Id} because block was not found after submission"));
        }*/
        return new SubmitResult(result, share.TransactionConfirmationData);
    }

    protected virtual void PostChainIdentifyConfigure()
    {
    }

    protected override void ConfigureDaemons()
    {
        // TODO: implement failover??
        var daemon = poolConfig.Daemons.First();
        var httpClient = new HttpClient();
        Node = new PandaniteNodeV1Api(httpClient, string.Join(":", daemon.Host, daemon.Port));
    }

    protected override async Task<bool> AreDaemonsHealthyAsync(CancellationToken ct)
    {
        /*if(hasLegacyDaemon)
            return await AreDaemonsHealthyLegacyAsync(ct);

        var response = await rpc.ExecuteAsync<BlockchainInfo>(logger, BitcoinCommands.GetBlockchainInfo, ct);

        return response.Error == null;*/
        await Task.CompletedTask;
        return true;
    }

    protected override async Task<bool> AreDaemonsConnectedAsync(CancellationToken ct)
    {
        var (success, _) = await Node.GetBlock();
        return success;
    }

    protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
    {
        /*using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        var syncPendingNotificationShown = false;

        do
        {
            var response = await rpc.ExecuteAsync<BlockTemplate>(logger,
                BitcoinCommands.GetBlockTemplate, ct, GetBlockTemplateParams());

            var isSynched = response.Error == null;

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
        } while(await timer.WaitForNextTickAsync(ct));*/
        await Task.CompletedTask;
    }

    protected override async Task PostStartInitAsync(CancellationToken ct)
    {
        /*var requests = new[]
        {
            new RpcRequest(BitcoinCommands.ValidateAddress, new[] { poolConfig.Address }),
            new RpcRequest(BitcoinCommands.SubmitBlock),
            new RpcRequest(!hasLegacyDaemon ? BitcoinCommands.GetBlockchainInfo : BitcoinCommands.GetInfo),
            new RpcRequest(BitcoinCommands.GetDifficulty),
            new RpcRequest(BitcoinCommands.GetAddressInfo, new[] { poolConfig.Address }),
        };

        var responses = await rpc.ExecuteBatchAsync(logger, ct, requests);

        if(responses.Any(x => x.Error != null))
        {
            // filter out optional RPCs
            var errors = responses
                .Where((x, i) => x.Error != null &&
                    requests[i].Method != BitcoinCommands.SubmitBlock &&
                    requests[i].Method != BitcoinCommands.GetAddressInfo)
                .ToArray();

            if(errors.Any())
                throw new PoolStartupException($"Init RPC failed: {string.Join(", ", errors.Select(y => y.Error.Message))}", poolConfig.Id);
        }

        // extract results
        var validateAddressResponse = responses[0].Error == null ? responses[0].Response.ToObject<ValidateAddressResponse>() : null;
        var submitBlockResponse = responses[1];
        var blockchainInfoResponse = !hasLegacyDaemon ? responses[2].Response.ToObject<BlockchainInfo>() : null;
        var daemonInfoResponse = hasLegacyDaemon ? responses[2].Response.ToObject<DaemonInfo>() : null;
        var difficultyResponse = responses[3].Response.ToObject<JToken>();
        var addressInfoResponse = responses[4].Error == null ? responses[4].Response.ToObject<AddressInfo>() : null;

        // chain detection
        if(!hasLegacyDaemon)
            network = Network.GetNetwork(blockchainInfoResponse.Chain.ToLower());
        else*/

        network = Network.Main;
        PostChainIdentifyConfigure();

        // ensure pool owns wallet
        /*if(validateAddressResponse is not {IsValid: true})
            throw new PoolStartupException($"Daemon reports pool-address '{poolConfig.Address}' as invalid", poolConfig.Id);*/

        isPoS = false;

        // Create pool address script from response
        /*if(!isPoS)
        {
            if(extraPoolConfig != null && extraPoolConfig.AddressType != BitcoinAddressType.Legacy)
                logger.Info(()=> $"Interpreting pool address {poolConfig.Address} as type {extraPoolConfig?.AddressType.ToString()}");

            poolAddressDestination = AddressToDestination(poolConfig.Address, extraPoolConfig?.AddressType);
        }

        else
            poolAddressDestination = new PubKey(poolConfig.PubKey ?? validateAddressResponse.PubKey);

        // Payment-processing setup
        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            // ensure pool owns wallet
            if(validateAddressResponse is {IsMine: false} && addressInfoResponse is {IsMine: false})
                logger.Warn(()=> $"Daemon does not own pool-address '{poolConfig.Address}'");
        }*/

        // update stats
        BlockchainStats.NetworkType = network.Name;
        BlockchainStats.RewardType = isPoS ? "POS" : "POW";

        await UpdateNetworkStatsAsync(ct);
 
        // Periodically update network stats
        Observable.Interval(TimeSpan.FromMinutes(10))
            .Select(_ => Observable.FromAsync(() =>
                Guard(()=> UpdateNetworkStatsAsync(ct),
                    ex => logger.Error(ex))))
            .Concat()
            .Subscribe();

        SetupCrypto();
        SetupJobUpdates(ct);
    }

    protected void SetupCrypto()
    {

    }

    protected abstract Task<(bool IsNew, bool Force)> UpdateJob(CancellationToken ct, bool forceUpdate, string via = null, string json = null);
    protected abstract object GetJobParamsForStratum(bool isNew);

    #region API-Surface

    public Network Network => network;
    public IObservable<object> Jobs { get; private set; }
    public BlockchainStats BlockchainStats { get; } = new();

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        extraPoolConfig = pc.Extra.SafeExtensionDataAs<PandanitePoolConfigExtra>();
        base.Configure(pc, cc);
    }

    public virtual async Task<bool> ValidateAddressAsync(string address, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(address))
            return false;

            await Task.CompletedTask;

        if (address.Length != 50) {
            return false;
        }

        try {
            address.ToByteArray();
            return true;
        } catch {
            return false;
        }
    }

    #endregion // API-Surface
}
