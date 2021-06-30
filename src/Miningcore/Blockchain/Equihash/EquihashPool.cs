using System;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Equihash.Configuration;
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

namespace Miningcore.Blockchain.Equihash
{
    [CoinFamily(CoinFamily.Equihash)]
    public class EquihashPool : PoolBase
    {
        public EquihashPool(IComponentContext ctx,
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

        protected EquihashJobManager manager;
        protected object currentJobParams;
        private double hashrateDivisor;
        private EquihashPoolConfigExtra extraConfig;

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            base.Configure(poolConfig, clusterConfig);

            extraConfig = poolConfig.Extra.SafeExtensionDataAs<EquihashPoolConfigExtra>();

            if(poolConfig.Template.As<EquihashCoinTemplate>().UsesZCashAddressFormat &&
                string.IsNullOrEmpty(extraConfig?.ZAddress))
                logger.ThrowLogPoolStartupException("Pool z-address is not configured");
        }

        /// <param name="ct"></param>
        /// <inheritdoc />
        protected override async Task SetupJobManager(CancellationToken ct)
        {
            manager = ctx.Resolve<EquihashJobManager>(
                new TypedParameter(typeof(IExtraNonceProvider), new EquihashExtraNonceProvider(clusterConfig.InstanceId)));

            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync(ct);

            if(poolConfig.EnableInternalStratum == true)
            {
                disposables.Add(manager.Jobs
                    .Select(job => Observable.FromAsync(async () =>
                    {
                        try
                        {
                            await OnNewJobAsync(job);
                        }

                        catch(Exception ex)
                        {
                            logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}");
                        }
                    }))
                    .Concat()
                    .Subscribe(_ => { }, ex =>
                    {
                        logger.Debug(ex, nameof(OnNewJobAsync));
                    }));

                // we need work before opening the gates
                await manager.Jobs.Take(1).ToTask(ct);
            }

            else
            {
                // keep updating NetworkStats
                disposables.Add(manager.Jobs.Subscribe());
            }

            hashrateDivisor = (double) new BigRational(manager.ChainConfig.Diff1BValue, EquihashConstants.ZCashDiff1b);
        }

        protected override async Task InitStatsAsync()
        {
            await base.InitStatsAsync();

            blockchainStats = manager.BlockchainStats;
        }

        protected async Task OnSubscribeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = connection.ContextAs<BitcoinWorkerContext>();

            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            var requestParams = request.ParamsAs<string[]>();

            var data = new object[]
                {
                    connection.ConnectionId,
                }
                .Concat(manager.GetSubscriberData(connection))
                .ToArray();

            await connection.RespondAsync(data, request.Id);

            // setup worker context
            context.IsSubscribed = true;
            context.UserAgent = requestParams?.Length > 0 ? requestParams[0].Trim() : null;
        }

        protected async Task OnAuthorizeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;

            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            var context = connection.ContextAs<BitcoinWorkerContext>();
            var requestParams = request.ParamsAs<string[]>();
            var workerValue = requestParams?.Length > 0 ? requestParams[0] : null;
            var password = requestParams?.Length > 1 ? requestParams[1] : null;
            var passParts = password?.Split(PasswordControlVarsSeparator);

            // extract worker/miner
            var split = workerValue?.Split('.');
            var minerName = split?.FirstOrDefault()?.Trim();
            var workerName = split?.Skip(1).FirstOrDefault()?.Trim() ?? string.Empty;

            // assumes that workerName is an address
            context.IsAuthorized = !string.IsNullOrEmpty(minerName) && await manager.ValidateAddressAsync(minerName, ct);
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
                if(staticDiff.HasValue &&
                    (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                        context.VarDiff == null && staticDiff.Value > context.Difficulty))
                {
                    context.VarDiff = null; // disable vardiff
                    context.SetDifficulty(staticDiff.Value);

                    logger.Info(() => $"[{connection.ConnectionId}] Setting static difficulty of {staticDiff.Value}");

                    await connection.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
                }

                // send intial update
                await connection.NotifyAsync(EquihashStratumMethods.SetTarget, new object[] { EncodeTarget(context.Difficulty) });
                await connection.NotifyAsync(BitcoinStratumMethods.MiningNotify, currentJobParams);
            }

            else
            {
                // respond
                await connection.RespondErrorAsync(StratumError.UnauthorizedWorker, "Authorization failed", request.Id, context.IsAuthorized);

                // issue short-time ban if unauthorized to prevent DDos on daemon (validateaddress RPC)
                logger.Info(() => $"[{connection.ConnectionId}] Banning unauthorized worker for 60 sec");

                banManager.Ban(connection.RemoteEndpoint.Address, TimeSpan.FromSeconds(60));

                CloseConnection(connection);
            }
        }

        protected virtual async Task OnSubmitAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;
            var context = connection.ContextAs<BitcoinWorkerContext>();

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
                var poolEndpoint = poolConfig.Ports[connection.PoolEndpoint.Port];

                var share = await manager.SubmitShareAsync(connection, requestParams, poolEndpoint.Difficulty, ct);

                await connection.RespondAsync(true, request.Id);

                // publish
                messageBus.SendMessage(new ClientShare(connection, share));

                // telemetry
                PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

                logger.Info(() => $"[{connection.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty, 3)}");

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
                logger.Info(() => $"[{connection.ConnectionId}] Share rejected: {ex.Message}");

                // banning
                ConsiderBan(connection, context, poolConfig.Banning);

                throw;
            }
        }

        private async Task OnSuggestTargetAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = connection.ContextAs<BitcoinWorkerContext>();

            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            var requestParams = request.ParamsAs<string[]>();
            var target = requestParams.FirstOrDefault();

            if(!string.IsNullOrEmpty(target))
            {
                if(System.Numerics.BigInteger.TryParse(target, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var targetBig))
                {
                    var newDiff = (double) new BigRational(manager.ChainConfig.Diff1BValue, targetBig);
                    var poolEndpoint = poolConfig.Ports[connection.PoolEndpoint.Port];

                    if(newDiff >= poolEndpoint.Difficulty)
                    {
                        context.EnqueueNewDifficulty(newDiff);
                        context.ApplyPendingDifficulty();

                        await connection.NotifyAsync(EquihashStratumMethods.SetTarget, new object[] { EncodeTarget(context.Difficulty) });
                    }

                    else
                        await connection.RespondErrorAsync(StratumError.Other, "suggested difficulty too low", request.Id);
                }

                else
                    await connection.RespondErrorAsync(StratumError.Other, "invalid target", request.Id);
            }

            else
                await connection.RespondErrorAsync(StratumError.Other, "invalid target", request.Id);
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

                    case EquihashStratumMethods.SuggestTarget:
                        await OnSuggestTargetAsync(connection, tsRequest);
                        break;

                    case BitcoinStratumMethods.ExtraNonceSubscribe:
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

        protected Task OnNewJobAsync(object jobParams)
        {
            currentJobParams = jobParams;

            logger.Info(() => "Broadcasting job");

            var tasks = ForEachConnection(async client =>
            {
                if(!client.IsAlive)
                    return;

                var context = client.ContextAs<BitcoinWorkerContext>();

                if(context.IsSubscribed && context.IsAuthorized)
                {
                    // check alive
                    var lastActivityAgo = clock.Now - context.LastActivity;

                    if(poolConfig.ClientConnectionTimeout > 0 &&
                        lastActivityAgo.TotalSeconds > poolConfig.ClientConnectionTimeout)
                    {
                        logger.Info(() => $"[{client.ConnectionId}] Booting zombie-worker (idle-timeout exceeded)");
                        CloseConnection(client);
                        return;
                    }

                    // varDiff: if the client has a pending difficulty change, apply it now
                    if(context.ApplyPendingDifficulty())
                        await client.NotifyAsync(EquihashStratumMethods.SetTarget, new object[] { EncodeTarget(context.Difficulty) });

                    // send job
                    await client.NotifyAsync(BitcoinStratumMethods.MiningNotify, currentJobParams);
                }
            });

            return Task.WhenAll(tasks);
        }

        public override double HashrateFromShares(double shares, double interval)
        {
            var multiplier = BitcoinConstants.Pow2x32;
            var result = shares * multiplier / interval / 1000000 * 2;

            result /= hashrateDivisor;
            return result;
        }

        protected override async Task OnVarDiffUpdateAsync(StratumConnection connection, double newDiff)
        {
            var context = connection.ContextAs<BitcoinWorkerContext>();

            context.EnqueueNewDifficulty(newDiff);

            // apply immediately and notify client
            if(context.HasPendingDifficulty)
            {
                context.ApplyPendingDifficulty();

                await connection.NotifyAsync(EquihashStratumMethods.SetTarget, new object[] { EncodeTarget(context.Difficulty) });
                await connection.NotifyAsync(BitcoinStratumMethods.MiningNotify, currentJobParams);
            }
        }

        protected override WorkerContextBase CreateClientContext()
        {
            return new BitcoinWorkerContext();
        }

        private string EncodeTarget(double difficulty)
        {
            return EquihashUtils.EncodeTarget(difficulty, manager.ChainConfig);
        }
    }
}
