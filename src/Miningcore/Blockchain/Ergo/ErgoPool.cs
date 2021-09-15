using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Numerics;
using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Ergo.Configuration;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Nicehash;
using Miningcore.Notifications.Messages;
using Miningcore.Persistence;
using Miningcore.Persistence.Repositories;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Ergo
{
    [CoinFamily(CoinFamily.Ergo)]
    public class ErgoPool : PoolBase
    {
        public ErgoPool(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            IMessageBus messageBus,
            NicehashService nicehashService) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, nicehashService)
        {
        }

        protected object[] currentJobParams;
        protected ErgoJobManager manager;
        private ErgoPoolConfigExtra extraPoolConfig;
        private ErgoCoinTemplate coin;

        protected virtual async Task OnSubscribeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            var context = connection.ContextAs<ErgoWorkerContext>();
            var requestParams = request.ParamsAs<string[]>();

            var data = new object[]
            {
                new object[]
                {
                    new object[] { BitcoinStratumMethods.SetDifficulty, connection.ConnectionId },
                    new object[] { BitcoinStratumMethods.MiningNotify, connection.ConnectionId }
                }
            }
            .Concat(manager.GetSubscriberData(connection))
            .ToArray();

            await connection.RespondAsync(data, request.Id);

            // setup worker context
            context.IsSubscribed = true;
            context.UserAgent = requestParams?.Length > 0 ? requestParams[0].Trim() : null;
        }

        protected virtual async Task OnAuthorizeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;

            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            var context = connection.ContextAs<ErgoWorkerContext>();
            var requestParams = request.ParamsAs<string[]>();
            var workerValue = requestParams?.Length > 0 ? requestParams[0] : null;
            var password = requestParams?.Length > 1 ? requestParams[1] : null;
            var passParts = password?.Split(PasswordControlVarsSeparator);

            // extract worker/miner
            var split = workerValue?.Split('.');
            var minerName = split?.FirstOrDefault()?.Trim();
            var workerName = split?.Skip(1).FirstOrDefault()?.Trim() ?? string.Empty;

            // assumes that minerName is an address
            context.IsAuthorized = await manager.ValidateAddress(minerName, ct);
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

                // Nicehash support
                var nicehashDiff = await GetNicehashStaticMinDiff(connection, context.UserAgent, coin.Name, coin.GetAlgorithmName());

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

                // send intial update
                await SendJob(connection, context, currentJobParams);
            }

            else
            {
                await connection.RespondErrorAsync(StratumError.UnauthorizedWorker, "Authorization failed", request.Id, context.IsAuthorized);

                // issue short-time ban if unauthorized to prevent DDos on daemon (validateaddress RPC)
                logger.Info(() => $"[{connection.ConnectionId}] Banning unauthorized worker {minerName} for {loginFailureBanTimeout.TotalSeconds} sec");

                banManager.Ban(connection.RemoteEndpoint.Address, loginFailureBanTimeout);

                CloseConnection(connection);
            }
        }

        protected virtual async Task OnSubmitAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;
            var context = connection.ContextAs<ErgoWorkerContext>();

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

                // submit
                var requestParams = request.ParamsAs<string[]>();
                var poolEndpoint = poolConfig.Ports[connection.LocalEndpoint.Port];

                var share = await manager.SubmitShareAsync(connection, requestParams, poolEndpoint.Difficulty, ct);

                await connection.RespondAsync(true, request.Id);

                // publish
                messageBus.SendMessage(new StratumShare(connection, share));

                // telemetry
                PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

                logger.Info(() => $"[{connection.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty * ErgoConstants.ShareMultiplier, 3)}");

                // update pool stats
                if(share.IsBlockCandidate)
                    poolStats.LastPoolBlockTime = clock.Now;

                // update client stats
                context.Stats.ValidShares++;
                await UpdateVarDiffAsync(connection);
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

        protected virtual Task OnNewJobAsync(object[] jobParams)
        {
            currentJobParams = jobParams;

            logger.Info(() => "Broadcasting job");

            return Guard(()=> Task.WhenAll(ForEachConnection(async connection =>
            {
                if(!connection.IsAlive)
                    return;

                var context = connection.ContextAs<ErgoWorkerContext>();

                if(!context.IsSubscribed || !context.IsAuthorized || CloseIfDead(connection, context))
                    return;

                // varDiff: if the client has a pending difficulty change, apply it now
                if(context.ApplyPendingDifficulty())
                    await SendJob(connection, context, currentJobParams);
            })), ex=> logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}"));
        }

        private async Task SendJob(StratumConnection connection, ErgoWorkerContext context, object[] jobParams)
        {
            // clone job params
            var jobParamsActual = new object[jobParams.Length];

            for(var i = 0; i < jobParamsActual.Length; i++)
                jobParamsActual[i] = jobParams[i];

            var target = new BigRational(BitcoinConstants.Diff1 * (BigInteger) (1 / context.Difficulty * 0x10000), 0x10000).GetWholePart();
            jobParamsActual[6] = target.ToString();

            // send static diff of 1 since actual diff gets pre-multiplied to target
            await connection.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { 1 });

            // send target
            await connection.NotifyAsync(BitcoinStratumMethods.MiningNotify, jobParamsActual);
        }

        public override double HashrateFromShares(double shares, double interval)
        {
            var multiplier = BitcoinConstants.Pow2x32 * ErgoConstants.ShareMultiplier;
            var result = shares * multiplier / interval;

            // add flat pool side hashrate bonus to account for miner dataset generation
            result *= 1.15;

            return result;
        }

        public override double ShareMultiplier => ErgoConstants.ShareMultiplier;

        #region Overrides

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            coin = poolConfig.Template.As<ErgoCoinTemplate>();
            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<ErgoPoolConfigExtra>();

            base.Configure(poolConfig, clusterConfig);
        }

        protected override async Task SetupJobManager(CancellationToken ct)
        {
            var extraNonce1Size = extraPoolConfig?.ExtraNonce1Size ?? 2;

            manager = ctx.Resolve<ErgoJobManager>(
                new TypedParameter(typeof(IExtraNonceProvider), new ErgoExtraNonceProvider(poolConfig.Id, extraNonce1Size, clusterConfig.InstanceId)));

            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync(ct);

            if(poolConfig.EnableInternalStratum == true)
            {
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
            }

            else
            {
                // keep updating NetworkStats
                disposables.Add(manager.Jobs.Subscribe());
            }
        }

        protected override async Task InitStatsAsync()
        {
            await base.InitStatsAsync();

            blockchainStats = manager.BlockchainStats;
        }

        protected override WorkerContextBase CreateWorkerContext()
        {
            return new ErgoWorkerContext();
        }

        protected override async Task OnRequestAsync(StratumConnection connection,
            Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;

            try
            {
                switch(request.Method)
                {
                    case BitcoinStratumMethods.Subscribe:
                        await OnSubscribeAsync(connection, tsRequest);
                        break;

                    case BitcoinStratumMethods.Authorize:
                        await OnAuthorizeAsync(connection, tsRequest, ct);
                        break;

                    case BitcoinStratumMethods.SubmitShare:
                        await OnSubmitAsync(connection, tsRequest, ct);
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

        protected override async Task<double?> GetNicehashStaticMinDiff(StratumConnection connection, string userAgent, string coinName, string algoName)
        {
            var result= await base.GetNicehashStaticMinDiff(connection, userAgent, coinName, algoName);

            // adjust value to fit with our target value calculation
            if(result.HasValue)
                result = result.Value / uint.MaxValue;

            return result;
        }

        protected override async Task OnVarDiffUpdateAsync(StratumConnection connection, double newDiff)
        {
            var context = connection.ContextAs<ErgoWorkerContext>();

            context.EnqueueNewDifficulty(newDiff);

            if(context.HasPendingDifficulty)
            {
                context.ApplyPendingDifficulty();

                await SendJob(connection, context, currentJobParams);
            }
        }

        #endregion // Overrides
    }
}
