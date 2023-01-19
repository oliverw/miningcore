using static System.Array;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using Autofac;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Conceal.Configuration;
using Miningcore.Blockchain.Conceal.DaemonRequests;
using Miningcore.Blockchain.Conceal.DaemonResponses;
using Miningcore.Blockchain.Conceal.StratumRequests;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Native;
using Miningcore.Notifications.Messages;
using Miningcore.Rest;
using Miningcore.Rpc;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Contract = Miningcore.Contracts.Contract;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Conceal;

public class ConcealJobManager : JobManagerBase<ConcealJob>
{
    public ConcealJobManager(
        IComponentContext ctx,
        IMasterClock clock,
        IHttpClientFactory httpClientFactory,
        IMessageBus messageBus) :
        base(ctx, messageBus)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(messageBus);

        this.clock = clock;
        this.httpClientFactory = httpClientFactory;
    }

    private byte[] instanceId;
    private DaemonEndpointConfig[] daemonEndpoints;
    private IHttpClientFactory httpClientFactory;
    private SimpleRestClient restClient;
    private RpcClient rpc;
    private RpcClient walletRpc;
    private readonly IMasterClock clock;
    private ConcealNetworkType networkType;
    private ConcealPoolConfigExtra extraPoolConfig;
    private ulong poolAddressBase58Prefix;
    private DaemonEndpointConfig[] walletDaemonEndpoints;
    private ConcealCoinTemplate coin;

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

                // init job
                job = new ConcealJob(blockTemplate, instanceId, NextJobId(), coin, poolConfig, clusterConfig, newHash);
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

    private async Task<RpcResponse<GetBlockTemplateResponse>> GetBlockTemplateAsync(CancellationToken ct)
    {
        var request = new GetBlockTemplateRequest
        {
            WalletAddress = poolConfig.Address,
            ReserveSize = ConcealConstants.ReserveSize
        };

        return await rpc.ExecuteAsync<GetBlockTemplateResponse>(logger, ConcealCommands.GetBlockTemplate, ct, request);
    }

    private RpcResponse<GetBlockTemplateResponse> GetBlockTemplateFromJson(string json)
    {
        var result = JsonConvert.DeserializeObject<JsonRpcResponse>(json);

        return new RpcResponse<GetBlockTemplateResponse>(result.ResultAs<GetBlockTemplateResponse>());
    }

    private async Task ShowDaemonSyncProgressAsync(CancellationToken ct)
    {
        var info = await restClient.Get<GetInfoResponse>(ConcealConstants.DaemonRpcGetInfoLocation, ct);
        
        if(info.Status != "OK")
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
            var coin = poolConfig.Template.As<ConcealCoinTemplate>();
            var info = await restClient.Get<GetInfoResponse>(ConcealConstants.DaemonRpcGetInfoLocation, ct);
            
            if(info.Status != "OK")
                logger.Warn(() => $"Error(s) refreshing network stats...");

            if(info.Status == "OK")
            {
                BlockchainStats.NetworkHashrate = info.TargetHeight > 0 ? (double) info.Difficulty / coin.DifficultyTarget : 0;
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
        var response = await rpc.ExecuteAsync<SubmitResponse>(logger, ConcealCommands.SubmitBlock, CancellationToken.None, new[] { blobHex });

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

    public ConcealCoinTemplate Coin => coin;

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        Contract.RequiresNonNull(pc);
        Contract.RequiresNonNull(cc);

        logger = LogUtil.GetPoolScopedLogger(typeof(JobManagerBase<ConcealJob>), pc);
        poolConfig = pc;
        clusterConfig = cc;
        extraPoolConfig = pc.Extra.SafeExtensionDataAs<ConcealPoolConfigExtra>();
        coin = pc.Template.As<ConcealCoinTemplate>();
        
        var NetworkTypeOverride = !string.IsNullOrEmpty(extraPoolConfig?.NetworkTypeOverride) ? extraPoolConfig.NetworkTypeOverride : "testnet";
        
        switch(NetworkTypeOverride.ToLower())
        {
            case "mainnet":
                networkType = ConcealNetworkType.Main;
                break;
            case "testnet":
                networkType = ConcealNetworkType.Test;
                break;
            default:
                throw new PoolStartupException($"Unsupport net type '{NetworkTypeOverride}'", poolConfig.Id);
        }
        
        // extract standard daemon endpoints
        daemonEndpoints = pc.Daemons
            .Where(x => string.IsNullOrEmpty(x.Category))
            .Select(x =>
            {
                if(string.IsNullOrEmpty(x.HttpPath))
                    x.HttpPath = ConcealConstants.DaemonRpcLocation;

                return x;
            })
            .ToArray();

        if(cc.PaymentProcessing?.Enabled == true && pc.PaymentProcessing?.Enabled == true)
        {
            // extract wallet daemon endpoints
            walletDaemonEndpoints = pc.Daemons
                .Where(x => x.Category?.ToLower() == ConcealConstants.WalletDaemonCategory)
                .Select(x =>
                {
                    if(string.IsNullOrEmpty(x.HttpPath))
                        x.HttpPath = ConcealConstants.DaemonRpcLocation;

                    return x;
                })
                .ToArray();

            if(walletDaemonEndpoints.Length == 0)
                throw new PoolStartupException("Wallet-RPC daemon is not configured (Daemon configuration for conceal-pools require an additional entry of category \'wallet' pointing to the wallet daemon)", pc.Id);
        }

        ConfigureDaemons();
    }

    public bool ValidateAddress(string address)
    {
        if(string.IsNullOrEmpty(address))
            return false;

        var addressPrefix = CryptonoteBindings.DecodeAddress(address);
        var addressIntegratedPrefix = CryptonoteBindings.DecodeIntegratedAddress(address);
        var coin = poolConfig.Template.As<ConcealCoinTemplate>();

        switch(networkType)
        {
            case ConcealNetworkType.Main:
                if(addressPrefix != coin.AddressPrefix)
                    return false;
                break;

            case ConcealNetworkType.Test:
                if(addressPrefix != coin.AddressPrefixTestnet)
                    return false;
                break;
        }

        return true;
    }

    public BlockchainStats BlockchainStats { get; } = new();

    public void PrepareWorkerJob(ConcealWorkerJob workerJob, out string blob, out string target)
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
        ConcealSubmitShareRequest request, ConcealWorkerJob workerJob, CancellationToken ct)
    {
        Contract.RequiresNonNull(worker);
        Contract.RequiresNonNull(request);

        var context = worker.ContextAs<ConcealWorkerContext>();

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

    #region Overrides

    protected override void ConfigureDaemons()
    {
        var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();
        
        restClient = new SimpleRestClient(httpClientFactory, "http://" + daemonEndpoints.First().Host.ToString() + ":" + daemonEndpoints.First().Port.ToString() + "/");
        rpc = new RpcClient(daemonEndpoints.First(), jsonSerializerSettings, messageBus, poolConfig.Id);

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            // also setup wallet daemon
            walletRpc = new RpcClient(walletDaemonEndpoints.First(), jsonSerializerSettings, messageBus, poolConfig.Id);
        }
    }

    protected override async Task<bool> AreDaemonsHealthyAsync(CancellationToken ct)
    {
        logger.Debug(() => "Checking if conceald daemon is healthy...");
        
        // test daemons
        var response = await restClient.Get<GetInfoResponse>(ConcealConstants.DaemonRpcGetInfoLocation, ct);
        if(response.Status != "OK")
        {
            logger.Debug(() => $"conceald daemon did not responded...");
            return false;
        }
        
        logger.Debug(() => $"{response.Status} - Incoming: {response.IncomingConnectionsCount} - Outgoing: {response.OutgoingConnectionsCount})");
        
        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            logger.Debug(() => "Checking if walletd daemon is healthy...");
            
            // test wallet daemons
            //var response2 = await walletRpc.ExecuteAsync<GetAddressResponse>(logger, ConcealWalletCommands.GetAddress, ct);
            var request2 = new GetBalanceRequest
            {
                Address = poolConfig.Address
            };

            var response2 = await walletRpc.ExecuteAsync<GetBalanceResponse>(logger, ConcealWalletCommands.GetBalance, ct, request2);
            
            if(response2.Error != null)
                logger.Debug(() => $"walletd daemon response: {response2.Error.Message} (Code {response2.Error.Code})");

            return response2.Error == null;
        }

        return true;
    }

    protected override async Task<bool> AreDaemonsConnectedAsync(CancellationToken ct)
    {
        logger.Debug(() => "Checking if conceald daemon is connected...");
        
        var response = await restClient.Get<GetInfoResponse>(ConcealConstants.DaemonRpcGetInfoLocation, ct);
        
        if(response.Status != "OK")
            logger.Debug(() => $"conceald daemon is not connected...");
        
        if(response.Status == "OK")
            logger.Debug(() => $"Peers connected - Incoming: {response.IncomingConnectionsCount} - Outgoing: {response.OutgoingConnectionsCount}");
        
        return response.Status == "OK" &&
            (response.OutgoingConnectionsCount + response.IncomingConnectionsCount) > 0;
    }

    protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        
        logger.Debug(() => "Checking if conceald daemon is synched...");

        var syncPendingNotificationShown = false;

        do
        {
            var request = new GetBlockTemplateRequest
            {
                WalletAddress = poolConfig.Address,
                ReserveSize = ConcealConstants.ReserveSize
            };

            var response = await rpc.ExecuteAsync<GetBlockTemplateResponse>(logger,
                ConcealCommands.GetBlockTemplate, ct, request);
            
            if(response.Error != null)
                logger.Debug(() => $"conceald daemon response: {response.Error.Message} (Code {response.Error.Code})");

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
        var coin = poolConfig.Template.As<ConcealCoinTemplate>();
        var infoResponse = await restClient.Get<GetInfoResponse>(ConcealConstants.DaemonRpcGetInfoLocation, ct);
        
        if(infoResponse.Status != "OK")
            throw new PoolStartupException($"Init RPC failed...", poolConfig.Id);

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            //var addressResponse = await walletRpc.ExecuteAsync<GetAddressResponse>(logger, ConcealWalletCommands.GetAddress, ct);
            var request2 = new GetAddressRequest
            {
            };

            var addressResponse = await walletRpc.ExecuteAsync<GetAddressResponse>(logger, ConcealWalletCommands.GetAddress, ct, request2);

            // ensure pool owns wallet
            //if(clusterConfig.PaymentProcessing?.Enabled == true && addressResponse.Response?.Address != poolConfig.Address)
            if(clusterConfig.PaymentProcessing?.Enabled == true && Exists(addressResponse.Response?.Address, element => element == poolConfig.Address) == false)
                throw new PoolStartupException($"Wallet-Daemon does not own pool-address '{poolConfig.Address}'", poolConfig.Id);
        }

        // address validation
        poolAddressBase58Prefix = CryptonoteBindings.DecodeAddress(poolConfig.Address);
        if(poolAddressBase58Prefix == 0)
            throw new PoolStartupException("Unable to decode pool-address", poolConfig.Id);

        switch(networkType)
        {
            case ConcealNetworkType.Main:
                if(poolAddressBase58Prefix != coin.AddressPrefix)
                    throw new PoolStartupException($"Invalid pool address prefix. Expected {coin.AddressPrefix}, got {poolAddressBase58Prefix}", poolConfig.Id);
                break;
            
            case ConcealNetworkType.Test:
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
        instanceId = new byte[ConcealConstants.InstanceIdSize];

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
                .Where(x => !string.IsNullOrEmpty(x.Extra.SafeExtensionDataAs<ConcealDaemonEndpointConfigExtra>()?.ZmqBlockNotifySocket))
                .ToDictionary(x => x, x =>
                {
                    var extra = x.Extra.SafeExtensionDataAs<ConcealDaemonEndpointConfigExtra>();
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