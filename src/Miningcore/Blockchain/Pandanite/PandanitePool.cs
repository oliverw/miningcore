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

namespace Miningcore.Blockchain.Pandanite;

[CoinFamily(CoinFamily.Pandanite)]
public class PandanitePool : PoolBase
{
    public PandanitePool(IComponentContext ctx,
        JsonSerializerSettings serializerSettings,
        IConnectionFactory cf,
        IMinerRepository minerRepository,
        IStatsRepository statsRepo,
        IMapper mapper,
        IMasterClock clock,
        IMessageBus messageBus,
        RecyclableMemoryStreamManager rmsm,
        NicehashService nicehashService) :
        base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, rmsm, nicehashService)
    {
        if(minerRepository is null)
        {
            throw new ArgumentNullException(nameof(minerRepository));
        }
        MinerRepository = minerRepository;
    }

    protected object currentJobParams;
    protected PandaniteJobManager manager;
    private PandaniteCoinTemplate coin;

    public IMinerRepository MinerRepository { get; }

    protected virtual async Task OnSubscribeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;

        if(request.Id == null)
            throw new StratumException(StratumError.MinusOne, "missing request id");

        var context = connection.ContextAs<PandaniteWorkerContext>();
        var requestParams = request.ParamsAs<string[]>();

        var data = new object[]
        {
            new object[]
            {
                new object[] { PandaniteStratumMethods.SetDifficulty, connection.ConnectionId },
                new object[] { PandaniteStratumMethods.MiningNotify, connection.ConnectionId }
            }
        }
        .Concat(manager.GetSubscriberData(connection))
        .ToArray();

        await connection.RespondAsync(data, request.Id);

        // setup worker context
        context.IsSubscribed = true;
        context.UserAgent = requestParams.FirstOrDefault()?.Trim();

        // Nicehash support
        var nicehashDiff = await GetNicehashStaticMinDiff(context, coin.Name, coin.GetAlgorithmName());

        if(nicehashDiff.HasValue)
        {
            logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using API supplied difficulty of {nicehashDiff.Value}");

            context.VarDiff = null; // disable vardiff
            context.SetDifficulty(nicehashDiff.Value);
        }

        // send intial update
        await connection.NotifyAsync(PandaniteStratumMethods.SetDifficulty, new object[] { context.Difficulty });
        await connection.NotifyAsync(PandaniteStratumMethods.MiningNotify, currentJobParams);
    }

    protected virtual async Task OnAuthorizeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;

        if(request.Id == null)
            throw new StratumException(StratumError.MinusOne, "missing request id");

        var context = connection.ContextAs<PandaniteWorkerContext>();
        var requestParams = request.ParamsAs<string[]>();
        var workerValue = requestParams?.Length > 0 ? requestParams[0] : null;
        var password = requestParams?.Length > 1 ? requestParams[1] : null;
        var passParts = password?.Split(PasswordControlVarsSeparator);

        // extract worker/miner
        var split = workerValue?.Split('.');
        var minerName = split?.FirstOrDefault()?.Trim();
        var workerName = split?.Skip(1).FirstOrDefault()?.Trim() ?? string.Empty;

        // assumes that minerName is an address
        context.IsAuthorized = await manager.ValidateAddressAsync(minerName, ct);
        context.Miner = minerName;
        context.Worker = workerName;

        if(context.IsAuthorized)
        {
            // respond
            await connection.RespondAsync(context.IsAuthorized, request.Id);

            // log association
            logger.Info(() => $"[{connection.ConnectionId}] Authorized worker {workerValue}");

            // extract control vars from password
            var staticDiff = GetStaticDiffFromPassparts(passParts);

            // Static diff
            if(staticDiff.HasValue &&
               (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                   context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(staticDiff.Value);

                logger.Info(() => $"[{connection.ConnectionId}] Setting static difficulty of {staticDiff.Value}");

                await connection.NotifyAsync(PandaniteStratumMethods.SetDifficulty, new object[] { context.Difficulty });
            }

            /* NOT SECURE
            var minPayout = GetMinPayoutFromPassparts(passParts);

            await cf.RunTx(async (con, tx) => {
                var minerSettings = new MinerSettings()
                {
                    PoolId = Config.Id,
                    Address = context.Miner,
                    PaymentThreshold = minPayout ?? 0
                };

                await MinerRepository.UpdateSettingsAsync(con, tx, minerSettings);
            });

            if (minPayout.HasValue && minPayout > Config.PaymentProcessing.MinimumPayment) {
                logger.Info(() => $"[{connection.ConnectionId}] Setting minimum payout to {minPayout} PDN");
            }*/
        }

        else
        {
            await connection.RespondErrorAsync(StratumError.UnauthorizedWorker, "Authorization failed", request.Id, context.IsAuthorized);

            if(clusterConfig?.Banning?.BanOnLoginFailure is null or true)
            {
                // issue short-time ban if unauthorized to prevent DDos on daemon (validateaddress RPC)
                logger.Info(() => $"[{connection.ConnectionId}] Banning unauthorized worker {minerName} for {loginFailureBanTimeout.TotalSeconds} sec");

                banManager.Ban(connection.RemoteEndpoint.Address, loginFailureBanTimeout);

                Disconnect(connection);
            }
        }
    }

    protected virtual async Task OnSubmitAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<PandaniteWorkerContext>();

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

            // check worker state
            context.LastActivity = clock.Now;

            // validate worker
            if(!context.IsAuthorized)
                throw new StratumException(StratumError.UnauthorizedWorker, "unauthorized worker");
            else if(!context.IsSubscribed)
                throw new StratumException(StratumError.NotSubscribed, "not subscribed");

            var requestParams = request.ParamsAs<string[]>();

            // submit
            var share = await manager.SubmitShareAsync(connection, requestParams, ct);
            await connection.RespondAsync(true, request.Id);

            // publish
            messageBus.SendMessage(new StratumShare(connection, share));

            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

            logger.Info(() => $"[{connection.ConnectionId}] Share accepted: D={Math.Log2(share.Difficulty)}");

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

    private async Task OnSuggestDifficultyAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        /*var request = tsRequest.Value;
        var context = connection.ContextAs<BitcoinWorkerContext>();

        // acknowledge
        await connection.RespondAsync(true, request.Id);

        try
        {
            var requestedDiff = (double) Convert.ChangeType(request.Params, TypeCode.Double)!;

            // client may suggest higher-than-base difficulty, but not a lower one
            var poolEndpoint = poolConfig.Ports[connection.LocalEndpoint.Port];

            if(requestedDiff > poolEndpoint.Difficulty)
            {
                context.SetDifficulty(requestedDiff);
                await connection.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });

                logger.Info(() => $"[{connection.ConnectionId}] Difficulty set to {requestedDiff} as requested by miner");
            }
        }

        catch(Exception ex)
        {
            logger.Error(ex, () => $"Unable to convert suggested difficulty {request.Params}");
        }*/
        await Task.CompletedTask;
    }

    private async Task OnConfigureMiningAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        /*var request = tsRequest.Value;
        var context = connection.ContextAs<BitcoinWorkerContext>();

        var requestParams = request.ParamsAs<JToken[]>();
        var extensions = requestParams[0].ToObject<string[]>();
        var extensionParams = requestParams[1].ToObject<Dictionary<string, JToken>>();
        var result = new Dictionary<string, object>();

        if(extensions != null)
        {
            foreach(var extension in extensions)
            {
                switch(extension)
                {
                    case BitcoinStratumExtensions.VersionRolling:
                        ConfigureVersionRolling(connection, context, extensionParams, result);
                        break;

                    case BitcoinStratumExtensions.MinimumDiff:
                        ConfigureMinimumDiff(connection, context, extensionParams, result);
                        break;
                }
            }
        }

        await connection.RespondAsync(result, request.Id);*/
        await Task.CompletedTask;
    }

    protected virtual async Task OnNewJobAsync(object jobParams)
    {
        currentJobParams = jobParams;

        logger.Info(() => $"Broadcasting job {((object[]) jobParams)[0]}");

        await Guard(() => ForEachMinerAsync(async (connection, ct) =>
        {
            var context = connection.ContextAs<PandaniteWorkerContext>();

            // varDiff: if the client has a pending difficulty change, apply it now
            if(context.ApplyPendingDifficulty())
                await connection.NotifyAsync(PandaniteStratumMethods.SetDifficulty, new object[] { context.Difficulty });

            // send job
            await connection.NotifyAsync(PandaniteStratumMethods.MiningNotify, currentJobParams);
        }));
    }

    public override double HashrateFromShares(double shares, double interval)
    {
        return shares / interval;
    }

    #region Overrides

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        coin = pc.Template.As<PandaniteCoinTemplate>();

        base.Configure(pc, cc);
    }

    protected override async Task SetupJobManager(CancellationToken ct)
    {
        manager = ctx.Resolve<PandaniteJobManager>();

        manager.Configure(poolConfig, clusterConfig);

        await manager.StartAsync(ct);

        disposables.Add(manager.Jobs
            .Select(job => Observable.FromAsync(() =>
                Guard(()=> OnNewJobAsync(job),
                    ex=> logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}"))))
            .Concat()
            .Subscribe(_ => { }, ex =>
            {
                logger.Debug(ex, nameof(OnNewJobAsync));
            }));

        // start with initial blocktemplate
        await manager.Jobs.Take(1).ToTask(ct);

        //disposables.Add(manager.Jobs.Subscribe());
    }

    protected override async Task InitStatsAsync(CancellationToken ct)
    {
        await base.InitStatsAsync(ct);

        blockchainStats = manager.BlockchainStats;
    }

    protected override WorkerContextBase CreateWorkerContext()
    {
        return new PandaniteWorkerContext();
    }

    protected override async Task OnRequestAsync(StratumConnection connection,
        Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;

        try
        {
            switch(request.Method)
            {
                case PandaniteStratumMethods.Subscribe:
                    await OnSubscribeAsync(connection, tsRequest);
                    break;

                case PandaniteStratumMethods.Authorize:
                    await OnAuthorizeAsync(connection, tsRequest, ct);
                    break;

                case PandaniteStratumMethods.SubmitShare:
                    await OnSubmitAsync(connection, tsRequest, ct);
                    break;

                case PandaniteStratumMethods.SuggestDifficulty:
                    await OnSuggestDifficultyAsync(connection, tsRequest);
                    break;

                case PandaniteStratumMethods.MiningConfigure:
                    await OnConfigureMiningAsync(connection, tsRequest);
                    // ignored
                    break;

                case PandaniteStratumMethods.ExtraNonceSubscribe:
                    await connection.RespondAsync(true, request.Id);
                    break;

                case PandaniteStratumMethods.GetTransactions:
                    // ignored
                    break;

                case PandaniteStratumMethods.MiningMultiVersion:
                    // ignored
                    break;

                default:
                    logger.Debug(() => $"[{connection.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    await connection.RespondErrorAsync(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        catch(StratumException ex)
        {
            await connection.RespondErrorAsync(ex.Code, ex.Message, request.Id, false);
        }
    }

    protected override async Task OnVarDiffUpdateAsync(StratumConnection connection, double newDiff, CancellationToken ct)
    {
        await base.OnVarDiffUpdateAsync(connection, newDiff, ct);

        if(connection.Context.ApplyPendingDifficulty())
        {
            await connection.NotifyAsync(PandaniteStratumMethods.SetDifficulty, new object[] { connection.Context.Difficulty });
            await connection.NotifyAsync(PandaniteStratumMethods.MiningNotify, currentJobParams);
        }
    }

    #endregion // Overrides
}
