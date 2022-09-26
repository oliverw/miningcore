using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using Autofac;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Cryptonote.Configuration;
using Miningcore.Blockchain.Cryptonote.DaemonRequests;
using Miningcore.Blockchain.Cryptonote.DaemonResponses;
using Miningcore.Blockchain.Cryptonote.StratumRequests;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Native;
using Miningcore.Notifications.Messages;
using Miningcore.Rpc;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Contract = Miningcore.Contracts.Contract;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Cryptonote;

public class CryptonoteJobManager : JobManagerBase<CryptonoteJob>
{
    public CryptonoteJobManager(
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

    private byte[] instanceId;
    private DaemonEndpointConfig[] daemonEndpoints;
    private RpcClient rpc;
    private RpcClient walletRpc;
    private readonly IMasterClock clock;
    private CryptonoteNetworkType networkType;
    private CryptonotePoolConfigExtra extraPoolConfig;
    private RandomX.randomx_flags? randomXFlagsOverride;
    private RandomX.randomx_flags? randomXFlagsAdd;
    private string currentSeedHash;
    private string randomXRealm;
    private ulong poolAddressBase58Prefix;
    private DaemonEndpointConfig[] walletDaemonEndpoints;
    private CryptonoteCoinTemplate coin;

    protected async Task<bool> UpdateJob(CancellationToken ct, string via = null, string json = null)
    {
        try
        {
            var response = string.IsNullOrEmpty(json) ? await GetBlockTemplateAsync(ct) : GetBlockTemplateFromJson(json);

            // may happen if daemon is currently not connected to peers
            if(response.Error != null)
            {
                logger.Warn(() => $"Unable to update job. Daemon responded with: {response.Error.Message} Code {response.Error.Code}");
                return false;
            }

            var blockTemplate = response.Response;
            var job = currentJob;
            var newHash = blockTemplate.Blob.HexToByteArray().AsSpan().Slice(7, 32).ToHexString();

            var isNew = job == null || newHash != job.PrevHash;

            if(isNew)
            {
                messageBus.NotifyChainHeight(poolConfig.Id, blockTemplate.Height, poolConfig.Template);

                if(via != null)
                    logger.Info(() => $"Detected new block {blockTemplate.Height} [{via}]");
                else
                    logger.Info(() => $"Detected new block {blockTemplate.Height}");

                UpdateHashParams(blockTemplate);

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

    private void UpdateHashParams(GetBlockTemplateResponse blockTemplate)
    {
        switch(coin.Hash)
        {
            case CryptonightHashType.RandomX:
            {
                // detect seed hash change
                if(currentSeedHash != blockTemplate.SeedHash)
                {
                    logger.Info(()=> $"Detected new seed hash {blockTemplate.SeedHash} starting @ height {blockTemplate.Height}");

                    if(poolConfig.EnableInternalStratum == true)
                    {
                        RandomX.WithLock(() =>
                        {
                            // delete old seed
                            if(currentSeedHash != null)
                                RandomX.DeleteSeed(randomXRealm, currentSeedHash);

                            // activate new one
                            currentSeedHash = blockTemplate.SeedHash;
                            RandomX.CreateSeed(randomXRealm, currentSeedHash, randomXFlagsOverride, randomXFlagsAdd, extraPoolConfig.RandomXVMCount);
                        });
                    }

                    else
                        currentSeedHash = blockTemplate.SeedHash;
                }

                break;
            }

            case CryptonightHashType.RandomARQ:
            {
                // detect seed hash change
                if(currentSeedHash != blockTemplate.SeedHash)
                {
                    logger.Info(()=> $"Detected new seed hash {blockTemplate.SeedHash} starting @ height {blockTemplate.Height}");

                    if(poolConfig.EnableInternalStratum == true)
                    {
                        RandomARQ.WithLock(() =>
                        {
                            // delete old seed
                            if(currentSeedHash != null)
                                RandomARQ.DeleteSeed(randomXRealm, currentSeedHash);

                            // activate new one
                            currentSeedHash = blockTemplate.SeedHash;
                            RandomARQ.CreateSeed(randomXRealm, currentSeedHash, randomXFlagsOverride, randomXFlagsAdd, extraPoolConfig.RandomXVMCount);
                        });
                    }

                    else
                        currentSeedHash = blockTemplate.SeedHash;
                }

                break;
            }
        }
    }

    private async Task<RpcResponse<GetBlockTemplateResponse>> GetBlockTemplateAsync(CancellationToken ct)
    {
        var request = new GetBlockTemplateRequest
        {
            WalletAddress = poolConfig.Address,
            ReserveSize = CryptonoteConstants.ReserveSize
        };

        return await rpc.ExecuteAsync<GetBlockTemplateResponse>(logger, CryptonoteCommands.GetBlockTemplate, ct, request);
    }

    private RpcResponse<GetBlockTemplateResponse> GetBlockTemplateFromJson(string json)
    {
        var result = JsonConvert.DeserializeObject<JsonRpcResponse>(json);

        return new RpcResponse<GetBlockTemplateResponse>(result.ResultAs<GetBlockTemplateResponse>());
    }

    private async Task ShowDaemonSyncProgressAsync(CancellationToken ct)
    {
        var response = await rpc.ExecuteAsync<GetInfoResponse>(logger, CryptonoteCommands.GetInfo, ct);
        var info = response.Response;

        if(info != null)
        {
            var lowestHeight = info.Height;

            var totalBlocks = info.TargetHeight;
            var percent = (double) lowestHeight / totalBlocks * 100;

            logger.Info(() => $"Daemon has downloaded {percent:0.00}% of blockchain from {info.OutgoingConnectionsCount} peers");
        }
    }

    private async Task UpdateNetworkStatsAsync(CancellationToken ct)
    {
        try
        {
            var response = await rpc.ExecuteAsync(logger, CryptonoteCommands.GetInfo, ct);

            if(response.Error != null)
                logger.Warn(() => $"Error(s) refreshing network stats: {response.Error.Message} (Code {response.Error.Code})");

            if(response.Response != null)
            {
                var info = response.Response.ToObject<GetInfoResponse>();

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
        var response = await rpc.ExecuteAsync<SubmitResponse>(logger, CryptonoteCommands.SubmitBlock, CancellationToken.None, new[] { blobHex });

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

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        Contract.RequiresNonNull(pc);
        Contract.RequiresNonNull(cc);

        logger = LogUtil.GetPoolScopedLogger(typeof(JobManagerBase<CryptonoteJob>), pc);
        poolConfig = pc;
        clusterConfig = cc;
        extraPoolConfig = pc.Extra.SafeExtensionDataAs<CryptonotePoolConfigExtra>();
        coin = pc.Template.As<CryptonoteCoinTemplate>();

        if(pc.EnableInternalStratum == true)
        {
            randomXRealm = !string.IsNullOrEmpty(extraPoolConfig.RandomXRealm) ? extraPoolConfig.RandomXRealm : pc.Id;
            randomXFlagsOverride = MakeRandomXFlags(extraPoolConfig.RandomXFlagsOverride);
            randomXFlagsAdd = MakeRandomXFlags(extraPoolConfig.RandomXFlagsAdd);
        }

        // extract standard daemon endpoints
        daemonEndpoints = pc.Daemons
            .Where(x => string.IsNullOrEmpty(x.Category))
            .Select(x =>
            {
                if(string.IsNullOrEmpty(x.HttpPath))
                    x.HttpPath = CryptonoteConstants.DaemonRpcLocation;

                return x;
            })
            .ToArray();

        if(cc.PaymentProcessing?.Enabled == true && pc.PaymentProcessing?.Enabled == true)
        {
            // extract wallet daemon endpoints
            walletDaemonEndpoints = pc.Daemons
                .Where(x => x.Category?.ToLower() == CryptonoteConstants.WalletDaemonCategory)
                .Select(x =>
                {
                    if(string.IsNullOrEmpty(x.HttpPath))
                        x.HttpPath = CryptonoteConstants.DaemonRpcLocation;

                    return x;
                })
                .ToArray();

            if(walletDaemonEndpoints.Length == 0)
                throw new PoolStartupException("Wallet-RPC daemon is not configured (Daemon configuration for monero-pools require an additional entry of category \'wallet' pointing to the wallet daemon)", pc.Id);
        }

        ConfigureDaemons();
    }

    public bool ValidateAddress(string address)
    {
        if(string.IsNullOrEmpty(address))
            return false;

        var addressPrefix = CryptonoteBindings.DecodeAddress(address);
        var addressIntegratedPrefix = CryptonoteBindings.DecodeIntegratedAddress(address);
        var coin = poolConfig.Template.As<CryptonoteCoinTemplate>();

        switch(networkType)
        {
            case CryptonoteNetworkType.Main:
                if(addressPrefix != coin.AddressPrefix &&
                   addressPrefix != coin.SubAddressPrefix &&
                   addressIntegratedPrefix != coin.AddressPrefixIntegrated)
                    return false;
                break;

            case CryptonoteNetworkType.Test:
                if(addressPrefix != coin.AddressPrefixTestnet &&
                   addressPrefix != coin.SubAddressPrefixTestnet &&
                   addressIntegratedPrefix != coin.AddressPrefixIntegratedTestnet)
                    return false;
                break;

            case CryptonoteNetworkType.Stage:
                if(addressPrefix != coin.AddressPrefixStagenet &&
                   addressPrefix != coin.SubAddressPrefixStagenet &&
                   addressIntegratedPrefix != coin.AddressPrefixIntegratedStagenet)
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
        CryptonoteSubmitShareRequest request, CryptonoteWorkerJob workerJob, CancellationToken ct)
    {
        Contract.RequiresNonNull(worker);
        Contract.RequiresNonNull(request);

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

    private RandomX.randomx_flags? MakeRandomXFlags(JToken token)
    {
        if(token == null)
            return null;

        if(token.Type == JTokenType.Integer)
            return (RandomX.randomx_flags) token.Value<ulong>();
        else if(token.Type == JTokenType.String)
        {
            RandomX.randomx_flags result = 0;
            var value = token.Value<string>();

            foreach(var flag in value.Split("|").Select(x=> x.Trim()).Where(x=> !string.IsNullOrEmpty(x)))
            {
                if(Enum.TryParse(typeof(RandomX.randomx_flags), flag, true, out var flagVal))
                    result |= (RandomX.randomx_flags) flagVal;
            }

            return result;
        }

        return null;
    }

    #region Overrides

    protected override void ConfigureDaemons()
    {
        var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

        rpc = new RpcClient(daemonEndpoints.First(), jsonSerializerSettings, messageBus, poolConfig.Id);

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            // also setup wallet daemon
            walletRpc = new RpcClient(walletDaemonEndpoints.First(), jsonSerializerSettings, messageBus, poolConfig.Id);
        }
    }

    protected override async Task<bool> AreDaemonsHealthyAsync(CancellationToken ct)
    {
        // test daemons
        var response = await rpc.ExecuteAsync<GetInfoResponse>(logger, CryptonoteCommands.GetInfo, ct);

        if(response.Error != null)
            return false;

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            // test wallet daemons
            var responses2 = await walletRpc.ExecuteAsync<object>(logger, CryptonoteWalletCommands.GetAddress, ct);

            return responses2.Error == null;
        }

        return true;
    }

    protected override async Task<bool> AreDaemonsConnectedAsync(CancellationToken ct)
    {
        var response = await rpc.ExecuteAsync<GetInfoResponse>(logger, CryptonoteCommands.GetInfo, ct);

        return response.Error == null && response.Response != null &&
            (response.Response.OutgoingConnectionsCount + response.Response.IncomingConnectionsCount) > 0;
    }

    protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        var syncPendingNotificationShown = false;

        do
        {
            var request = new GetBlockTemplateRequest
            {
                WalletAddress = poolConfig.Address,
                ReserveSize = CryptonoteConstants.ReserveSize
            };

            var response = await rpc.ExecuteAsync<GetBlockTemplateResponse>(logger,
                CryptonoteCommands.GetBlockTemplate, ct, request);

            var isSynched = response.Error is not {Code: -9};

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
        SetInstanceId();

        // coin config
        var coin = poolConfig.Template.As<CryptonoteCoinTemplate>();
        var infoResponse = await rpc.ExecuteAsync(logger, CryptonoteCommands.GetInfo, ct);

        if(infoResponse.Error != null)
            throw new PoolStartupException($"Init RPC failed: {infoResponse.Error.Message} (Code {infoResponse.Error.Code})", poolConfig.Id);

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            var addressResponse = await walletRpc.ExecuteAsync<GetAddressResponse>(logger, CryptonoteWalletCommands.GetAddress, ct);

            // ensure pool owns wallet
            if(clusterConfig.PaymentProcessing?.Enabled == true && addressResponse.Response?.Address != poolConfig.Address)
                throw new PoolStartupException($"Wallet-Daemon does not own pool-address '{poolConfig.Address}'", poolConfig.Id);
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
                    throw new PoolStartupException($"Unsupport net type '{info.NetType}'", poolConfig.Id);
            }
        }

        else
            networkType = info.IsTestnet ? CryptonoteNetworkType.Test : CryptonoteNetworkType.Main;

        // address validation
        poolAddressBase58Prefix = CryptonoteBindings.DecodeAddress(poolConfig.Address);
        if(poolAddressBase58Prefix == 0)
            throw new PoolStartupException("Unable to decode pool-address", poolConfig.Id);

        switch(networkType)
        {
            case CryptonoteNetworkType.Main:
                if(poolAddressBase58Prefix != coin.AddressPrefix)
                    throw new PoolStartupException($"Invalid pool address prefix. Expected {coin.AddressPrefix}, got {poolAddressBase58Prefix}", poolConfig.Id);
                break;

            case CryptonoteNetworkType.Stage:
                if(poolAddressBase58Prefix != coin.AddressPrefixStagenet)
                    throw new PoolStartupException($"Invalid pool address prefix. Expected {coin.AddressPrefixStagenet}, got {poolAddressBase58Prefix}", poolConfig.Id);
                break;

            case CryptonoteNetworkType.Test:
                if(poolAddressBase58Prefix != coin.AddressPrefixTestnet)
                    throw new PoolStartupException($"Invalid pool address prefix. Expected {coin.AddressPrefixTestnet}, got {poolAddressBase58Prefix}", poolConfig.Id);
                break;
        }

        // update stats
        BlockchainStats.RewardType = "POW";
        BlockchainStats.NetworkType = networkType.ToString();

        await UpdateNetworkStatsAsync(ct);

        // Periodically update network stats
        Observable.Interval(TimeSpan.FromMinutes(1))
            .Select(via => Observable.FromAsync(() =>
                Guard(()=> UpdateNetworkStatsAsync(ct),
                    ex=> logger.Error(ex))))
            .Concat()
            .Subscribe();

        SetupJobUpdates(ct);
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

    protected virtual void SetupJobUpdates(CancellationToken ct)
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

                var blockNotify = rpc.ZmqSubscribe(logger, ct, zmq)
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

        Blocks = triggers.Merge()
            .Select(x => Observable.FromAsync(() => UpdateJob(ct, x.Via, x.Data)))
            .Concat()
            .Where(isNew => isNew)
            .Do(_ => hasInitialBlockTemplate = true)
            .Select(_ => Unit.Default)
            .Publish()
            .RefCount();
    }

    #endregion // Overrides
}
