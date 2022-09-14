using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Autofac;
using AutoMapper;
using Microsoft.IO;
using Miningcore.Configuration;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Nicehash;
using Miningcore.Notifications.Messages;
using Miningcore.Persistence;
using Miningcore.Persistence.Repositories;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Ethereum;

[CoinFamily(CoinFamily.Ethereum)]
public class EthereumPool : PoolBase
{
    public EthereumPool(IComponentContext ctx,
        JsonSerializerSettings serializerSettings,
        IConnectionFactory cf,
        IStatsRepository statsRepo,
        IMapper mapper,
        IMasterClock clock,
        IMessageBus messageBus,
        RecyclableMemoryStreamManager rmsm,
        NicehashService nicehashService) :
        base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, rmsm, nicehashService)
    {
    }

    private EthereumJobManager manager;
    private EthereumCoinTemplate coin;

    #region // Protocol V2 handlers - https://github.com/nicehash/Specifications/blob/master/EthereumStratum_NiceHash_v1.0.0.txt

    private async Task OnSubscribeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<EthereumWorkerContext>();

        if(request.Id == null)
            throw new StratumException(StratumError.Other, "missing request id");

        var requestParams = request.ParamsAs<string[]>();

        if(requestParams == null || requestParams.Length < 2 || requestParams.Any(string.IsNullOrEmpty))
            throw new StratumException(StratumError.MinusOne, "invalid request");

        manager.PrepareWorker(connection);

        context.UserAgent = requestParams.FirstOrDefault()?.Trim();

        var data = new object[]
        {
            new object[]
            {
                EthereumStratumMethods.MiningNotify,
                connection.ConnectionId,
                EthereumConstants.EthereumStratumVersion
            },
            context.ExtraNonce1
        }
        .ToArray();

        // Nicehash's stupid validator insists on "error" property present
        // in successful responses which is a violation of the JSON-RPC spec
        var response = new JsonRpcResponse<object[]>(data, request.Id);

        if(context.IsNicehash)
        {
            response.Extra = new Dictionary<string, object>();
            response.Extra["error"] = null;
        }

        await connection.RespondAsync(response);

        // setup worker context
        context.IsSubscribed = true;
    }

    private async Task OnAuthorizeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<EthereumWorkerContext>();

        if(request.Id == null)
            throw new StratumException(StratumError.MinusOne, "missing request id");

        var requestParams = request.ParamsAs<string[]>();
        var workerValue = requestParams?.Length > 0 ? requestParams[0] : "0";
        var password = requestParams?.Length > 1 ? requestParams[1] : null;
        var passParts = password?.Split(PasswordControlVarsSeparator);

        // extract worker/miner
        var workerParts = workerValue?.Split('.');
        var minerName = workerParts?.Length > 0 ? workerParts[0].Trim() : null;
        var workerName = workerParts?.Length > 1 ? workerParts[1].Trim() : "0";

        context.IsAuthorized = manager.ValidateAddress(minerName);

        // respond
        await connection.RespondAsync(context.IsAuthorized, request.Id);

        if(context.IsAuthorized)
        {
            context.Miner = minerName?.ToLower();
            context.Worker = workerName;

            // extract control vars from password
            var staticDiff = GetStaticDiffFromPassparts(passParts);

            // Nicehash support
            var nicehashDiff = await GetNicehashStaticMinDiff(context, coin.Name, coin.GetAlgorithmName());

            if(nicehashDiff.HasValue)
            {
                if(!staticDiff.HasValue || nicehashDiff > staticDiff)
                {
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using API supplied difficulty of {nicehashDiff.Value}");

                    staticDiff = nicehashDiff;
                }

                else
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using miner supplied difficulty of {staticDiff.Value}");
            }

            // Static diff
            if(staticDiff.HasValue &&
               (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                   context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(staticDiff.Value);

                logger.Info(() => $"[{connection.ConnectionId}] Setting static difficulty of {staticDiff.Value}");
            }

            await connection.NotifyAsync(EthereumStratumMethods.SetDifficulty, new object[] { context.Difficulty });
            await connection.NotifyAsync(EthereumStratumMethods.MiningNotify, manager.GetJobParamsForStratum());

            logger.Info(() => $"[{connection.ConnectionId}] Authorized worker {workerValue}");
        }

        else
        {
            if(clusterConfig?.Banning?.BanOnLoginFailure is null or true)
            {
                logger.Info(() => $"[{connection.ConnectionId}] Banning unauthorized worker {minerName} for {loginFailureBanTimeout.TotalSeconds} sec");

                banManager.Ban(connection.RemoteEndpoint.Address, loginFailureBanTimeout);

                Disconnect(connection);
            }
        }
    }

    private async Task OnSubmitAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct, bool v1 = false)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<EthereumWorkerContext>();

        try
        {
            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            // check age of submission (aged submissions are usually caused by high server load)
            var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

            if(requestAge > maxShareAge)
            {
                logger.Warn(() => $"[{connection.ConnectionId}] Dropping stale share submission request (server overloaded?)");
                return;
            }

            // validate worker
            if(!context.IsAuthorized)
                throw new StratumException(StratumError.UnauthorizedWorker, "unauthorized worker");
            if(!context.IsSubscribed)
                throw new StratumException(StratumError.NotSubscribed, "not subscribed");

            // check request
            var submitRequest = request.ParamsAs<string[]>();

            if(submitRequest.Length != 3 ||
               submitRequest.Any(string.IsNullOrEmpty))
                throw new StratumException(StratumError.MinusOne, "malformed PoW result");

            // recognize activity
            context.LastActivity = clock.Now;

            // submit
            Share share;

            if(!v1)
                share = await manager.SubmitShareV2Async(connection, submitRequest, ct);
            else
                share = await manager.SubmitShareV1Async(connection, submitRequest, GetWorkerNameFromV1Request(request, context), ct);

            await connection.RespondAsync(true, request.Id);

            // publish
            messageBus.SendMessage(new StratumShare(connection, share));

            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

            logger.Info(() => $"[{connection.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty / EthereumConstants.Pow2x32, 3)}");

            // update pool stats
            if(share.IsBlockCandidate)
                poolStats.LastPoolBlockTime = clock.Now;

            // update client stats
            context.Stats.ValidShares++;

            await UpdateVarDiffAsync(connection, false, ct);
        }

        catch(StratumException ex)
        {
            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, false);

            // update client stats
            context.Stats.InvalidShares++;
            logger.Info(() => $"[{connection.ConnectionId}] Share rejected: {ex.Message} [{context.UserAgent}]");

            // banning
            ConsiderBan(connection, context, poolConfig.Banning);

            throw;
        }
    }

    private async Task SendJob(EthereumWorkerContext context, StratumConnection connection, object parameters)
    {
        // varDiff: if the client has a pending difficulty change, apply it now
        if(context.ApplyPendingDifficulty())
            await connection.NotifyAsync(EthereumStratumMethods.SetDifficulty, new object[] { context.Difficulty });

        // send job
        await connection.NotifyAsync(EthereumStratumMethods.MiningNotify, parameters);
    }

    #endregion // Protocol V2 handlers

    #region // Protocol V1 handlers - https://github.com/sammy007/open-ethereum-pool/blob/master/docs/STRATUM.md

    private async Task OnSubmitLoginAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<EthereumWorkerContext>();

        if(request.Id == null)
            throw new StratumException(StratumError.Other, "missing request id");

        var requestParams = request.ParamsAs<string[]>();

        if(requestParams?.Length < 1)
            throw new StratumException(StratumError.MinusOne, "invalid request");

        var workerValue = requestParams?.Length > 0 ? requestParams[0] : "0";
        var password = requestParams?.Length > 1 ? requestParams[1] : null;
        var passParts = password?.Split(PasswordControlVarsSeparator);

        // extract worker/miner
        var workerParts = workerValue?.Split('.');
        var minerName = workerParts?.Length > 0 ? workerParts[0].Trim() : null;
        var workerName = workerParts?.Length > 1 ? workerParts[1].Trim() : "0";

        manager.PrepareWorker(connection);

        context.IsAuthorized = manager.ValidateAddress(minerName);

        // respond
        await connection.RespondAsync(context.IsAuthorized, request.Id);

        if(context.IsAuthorized)
        {
            context.Miner = minerName?.ToLower();
            context.Worker = workerName;

            // extract control vars from password
            var staticDiff = GetStaticDiffFromPassparts(passParts);

            // Nicehash support
            var nicehashDiff = await GetNicehashStaticMinDiff(context, coin.Name, coin.GetAlgorithmName());

            if(nicehashDiff.HasValue)
            {
                if(!staticDiff.HasValue || nicehashDiff > staticDiff)
                {
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using API supplied difficulty of {nicehashDiff.Value}");

                    staticDiff = nicehashDiff;
                }

                else
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using miner supplied difficulty of {staticDiff.Value}");
            }

            // Static diff
            if(staticDiff.HasValue &&
               (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                   context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(staticDiff.Value);

                logger.Info(() => $"[{connection.ConnectionId}] Setting static difficulty of {staticDiff.Value}");
            }

            logger.Info(() => $"[{connection.ConnectionId}] Authorized worker {workerValue}");

            // setup worker context
            context.IsSubscribed = true;
        }

        else
        {
            if(clusterConfig?.Banning?.BanOnLoginFailure is null or true)
            {
                banManager.Ban(connection.RemoteEndpoint.Address, loginFailureBanTimeout);

                logger.Info(() => $"[{connection.ConnectionId}] Banning unauthorized worker {minerName} for {loginFailureBanTimeout.TotalSeconds} sec");

                Disconnect(connection);
            }
        }
    }

    private async Task OnGetWorkAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<EthereumWorkerContext>();

        await SendWork(context, connection, request.Id);
    }

    private async Task SendWork(EthereumWorkerContext context, StratumConnection connection, object requestId)
    {
        var parameters = manager.GetWorkParamsForStratum(context);

        // respond
        await connection.RespondAsync(parameters, requestId);
    }

    #endregion // Protocol V1 handlers

    #region Overrides

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        coin = pc.Template.As<EthereumCoinTemplate>();

        base.Configure(pc, cc);
    }

    protected override async Task SetupJobManager(CancellationToken ct)
    {
        manager = ctx.Resolve<EthereumJobManager>(
            new TypedParameter(typeof(IExtraNonceProvider), new EthereumExtraNonceProvider(poolConfig.Id, clusterConfig.InstanceId)));

        manager.Configure(poolConfig, clusterConfig);

        await manager.StartAsync(ct);

        if(poolConfig.EnableInternalStratum == true)
        {
            disposables.Add(manager.Jobs
                .Select(_ => Observable.FromAsync(() =>
                    Guard(OnNewJobAsync,
                        ex=> logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}"))))
                .Concat()
                .Subscribe(_ => { }, ex =>
                {
                    logger.Debug(ex, nameof(OnNewJobAsync));
                }));

            // start with initial blocktemplate
            await manager.Jobs.Take(1).ToTask(ct);
        }

        else
        {
            // keep updating NetworkStats
            disposables.Add(manager.Jobs.Subscribe());
        }
    }

    protected override async Task InitStatsAsync(CancellationToken ct)
    {
        await base.InitStatsAsync(ct);

        blockchainStats = manager.BlockchainStats;
    }

    protected override WorkerContextBase CreateWorkerContext()
    {
        return new EthereumWorkerContext();
    }

    private static string GetWorkerNameFromV1Request(JsonRpcRequest request, EthereumWorkerContext context)
    {
        if(request.Extra?.TryGetValue(EthereumConstants.RpcRequestWorkerPropertyName, out var tmp) == true && tmp is string workerNameValue)
            return workerNameValue;

        return context.Worker;
    }

    protected virtual async Task OnNewJobAsync()
    {
        var currentJobParams = manager.GetJobParamsForStratum();

        logger.Info(() => $"Broadcasting job {currentJobParams[0]}");

        await Guard(() => ForEachMinerAsync(async (connection, ct) =>
        {
            var context = connection.ContextAs<EthereumWorkerContext>();

            switch(context.ProtocolVersion)
            {
                case 1:
                    await SendWork(context, connection, 0);
                    break;

                case 2:
                    await SendJob(context, connection, currentJobParams);
                    break;
            }
        }));
    }

    protected void EnsureProtocolVersion(EthereumWorkerContext context, int version)
    {
        if(context.ProtocolVersion != version)
            throw new StratumException(StratumError.MinusOne, $"protocol mismatch");
    }

    protected override async Task OnRequestAsync(StratumConnection connection,
        Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<EthereumWorkerContext>();

        try
        {
            switch(request.Method)
            {
                // V2/Nicehash Stratum Methods
                case EthereumStratumMethods.Subscribe:
                    context.ProtocolVersion = 2;    // lock in protocol version

                    await OnSubscribeAsync(connection, tsRequest);
                    break;

                case EthereumStratumMethods.Authorize:
                    EnsureProtocolVersion(context, 2);

                    await OnAuthorizeAsync(connection, tsRequest);
                    break;

                case EthereumStratumMethods.SubmitShare:
                    EnsureProtocolVersion(context, 2);

                    await OnSubmitAsync(connection, tsRequest, ct);
                    break;

                case EthereumStratumMethods.ExtraNonceSubscribe:
                    EnsureProtocolVersion(context, 2);

                    // Pretend to support it even though we actually do not. Some miners drop the connection upon receiving an error from this
                    await connection.RespondAsync(true, request.Id);
                    break;

                // V1 Stratum methods
                case EthereumStratumMethods.SubmitLogin:
                    context.ProtocolVersion = 1;    // lock in protocol version

                    await OnSubmitLoginAsync(connection, tsRequest);
                    break;

                case EthereumStratumMethods.GetWork:
                    EnsureProtocolVersion(context, 1);

                    await OnGetWorkAsync(connection, tsRequest);
                    break;

                case EthereumStratumMethods.SubmitWork:
                    EnsureProtocolVersion(context, 1);

                    await OnSubmitAsync(connection, tsRequest, ct, true);
                    break;

                case EthereumStratumMethods.SubmitHashrate:
                    await connection.RespondAsync(true, request.Id);
                    break;

                default:
                    logger.Info(() => $"[{connection.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    await connection.RespondErrorAsync(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        catch(StratumException ex)
        {
            await connection.RespondErrorAsync(ex.Code, ex.Message, request.Id, false);
        }
    }

    public override double HashrateFromShares(double shares, double interval)
    {
        var result = shares / interval;
        return result;
    }

    public override double ShareMultiplier => 1;

    protected override async Task OnVarDiffUpdateAsync(StratumConnection connection, double newDiff, CancellationToken ct)
    {
        await base.OnVarDiffUpdateAsync(connection, newDiff, ct);

        var context = connection.ContextAs<EthereumWorkerContext>();

        if(context.HasPendingDifficulty)
        {
            switch(context.ProtocolVersion)
            {
                case 1:
                    context.ApplyPendingDifficulty();

                    await SendWork(context, connection, 0);
                    break;

                case 2:
                    await SendJob(context, connection, manager.GetJobParamsForStratum());
                    break;
            }
        }
    }

    #endregion // Overrides
}
