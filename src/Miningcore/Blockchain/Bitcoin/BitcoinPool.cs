using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Nicehash;
using Miningcore.Nicehash.API;
using Miningcore.Notifications.Messages;
using Miningcore.Persistence;
using Miningcore.Persistence.Repositories;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace Miningcore.Blockchain.Bitcoin
{
    [CoinFamily(CoinFamily.Bitcoin)]
    public class BitcoinPool : PoolBase
    {
        public BitcoinPool(IComponentContext ctx,
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

        protected object currentJobParams;
        protected BitcoinJobManager manager;
        private BitcoinTemplate coin;

        protected virtual async Task OnSubscribeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            var context = connection.ContextAs<BitcoinWorkerContext>();
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

            // send intial update
            await connection.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
            await connection.NotifyAsync(BitcoinStratumMethods.MiningNotify, currentJobParams);
        }

        protected virtual async Task OnAuthorizeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
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

            // assumes that minerName is an address
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

                // Nicehash support
                if(clusterConfig.Nicehash?.EnableAutoDiff == true &&
                   context.UserAgent.Contains(NicehashConstants.NicehashUA, StringComparison.OrdinalIgnoreCase))
                {
                    // query current diff
                    var nicehashDiff = await nicehashService.GetStaticDiff(coin.Name, coin.GetAlgorithmName(), CancellationToken.None);

                    if(nicehashDiff.HasValue)
                    {
                        if(!staticDiff.HasValue || nicehashDiff > staticDiff)
                        {
                            logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using API supplied difficulty of {nicehashDiff.Value}");

                            staticDiff = nicehashDiff;
                        }

                        else
                            logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using custom difficulty of {staticDiff.Value}");
                    }
                }

                // Static diff
                if(staticDiff.HasValue &&
                   (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                    context.VarDiff == null && staticDiff.Value > context.Difficulty))
                {
                    context.VarDiff = null; // disable vardiff
                    context.SetDifficulty(staticDiff.Value);

                    logger.Info(() => $"[{connection.ConnectionId}] Setting static difficulty of {staticDiff.Value}");

                    await connection.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
                }
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

                logger.Info(() => $"[{connection.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty * coin.ShareMultiplier, 3)}");

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

        private async Task OnSuggestDifficultyAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = connection.ContextAs<BitcoinWorkerContext>();

            // acknowledge
            await connection.RespondAsync(true, request.Id);

            try
            {
                var requestedDiff = (double) Convert.ChangeType(request.Params, TypeCode.Double);

                // client may suggest higher-than-base difficulty, but not a lower one
                var poolEndpoint = poolConfig.Ports[connection.PoolEndpoint.Port];

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
            }
        }

        private async Task OnConfigureMiningAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = connection.ContextAs<BitcoinWorkerContext>();

            var requestParams = request.ParamsAs<JToken[]>();
            var extensions = requestParams[0].ToObject<string[]>();
            var extensionParams = requestParams[1].ToObject<Dictionary<string, JToken>>();
            var result = new Dictionary<string, object>();

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

            await connection.RespondAsync(result, request.Id);
        }

        private void ConfigureVersionRolling(StratumConnection connection, BitcoinWorkerContext context,
            IReadOnlyDictionary<string, JToken> extensionParams, Dictionary<string, object> result)
        {
            //var requestedBits = extensionParams[BitcoinStratumExtensions.VersionRollingBits].Value<int>();
            var requestedMask = BitcoinConstants.VersionRollingPoolMask;

            if(extensionParams.TryGetValue(BitcoinStratumExtensions.VersionRollingMask, out var requestedMaskValue))
                requestedMask = uint.Parse(requestedMaskValue.Value<string>(), NumberStyles.HexNumber);

            // Compute effective mask
            context.VersionRollingMask = BitcoinConstants.VersionRollingPoolMask & requestedMask;

            // enabled
            result[BitcoinStratumExtensions.VersionRolling] = true;
            result[BitcoinStratumExtensions.VersionRollingMask] = context.VersionRollingMask.Value.ToStringHex8();

            logger.Info(() => $"[{connection.ConnectionId}] Using version-rolling mask {result[BitcoinStratumExtensions.VersionRollingMask]}");
        }

        private void ConfigureMinimumDiff(StratumConnection connection, BitcoinWorkerContext context,
            IReadOnlyDictionary<string, JToken> extensionParams, Dictionary<string, object> result)
        {
            var requestedDiff = extensionParams[BitcoinStratumExtensions.MinimumDiffValue].Value<double>();

            // client may suggest higher-than-base difficulty, but not a lower one
            var poolEndpoint = poolConfig.Ports[connection.PoolEndpoint.Port];

            if(requestedDiff > poolEndpoint.Difficulty)
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(requestedDiff);

                logger.Info(() => $"[{connection.ConnectionId}] Difficulty set to {requestedDiff} as requested by miner. VarDiff now disabled.");

                // enabled
                result[BitcoinStratumExtensions.MinimumDiff] = true;
            }
        }

        protected virtual async Task OnNewJobAsync(object jobParams)
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
                        await client.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });

                    // send job
                    await client.NotifyAsync(BitcoinStratumMethods.MiningNotify, currentJobParams);
                }
            });

            try
            {
                await Task.WhenAll(tasks);
            }

            catch(Exception ex)
            {
                logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}");
            }
        }

        public override double HashrateFromShares(double shares, double interval)
        {
            var multiplier = BitcoinConstants.Pow2x32;
            var result = shares * multiplier / interval;

            //result *= coin.HashrateMultiplier;

            return result;
        }

        #region Overrides

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            coin = poolConfig.Template.As<BitcoinTemplate>();

            base.Configure(poolConfig, clusterConfig);
        }

        protected override async Task SetupJobManager(CancellationToken ct)
        {
            manager = ctx.Resolve<BitcoinJobManager>(
                new TypedParameter(typeof(IExtraNonceProvider), new BitcoinExtraNonceProvider(clusterConfig.InstanceId)));

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
        }

        protected override async Task InitStatsAsync()
        {
            await base.InitStatsAsync();

            blockchainStats = manager.BlockchainStats;
        }

        protected override WorkerContextBase CreateClientContext()
        {
            return new BitcoinWorkerContext();
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

                    case BitcoinStratumMethods.SuggestDifficulty:
                        await OnSuggestDifficultyAsync(connection, tsRequest);
                        break;

                    case BitcoinStratumMethods.MiningConfigure:
                        await OnConfigureMiningAsync(connection, tsRequest);
                        // ignored
                        break;

                    case BitcoinStratumMethods.ExtraNonceSubscribe:
                        await connection.RespondAsync(true, request.Id);
                        break;

                    case BitcoinStratumMethods.GetTransactions:
                        // ignored
                        break;

                    case BitcoinStratumMethods.MiningMultiVersion:
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

        protected override async Task OnVarDiffUpdateAsync(StratumConnection connection, double newDiff)
        {
            var context = connection.ContextAs<BitcoinWorkerContext>();
            context.EnqueueNewDifficulty(newDiff);

            // apply immediately and notify client
            if(context.HasPendingDifficulty)
            {
                context.ApplyPendingDifficulty();

                await connection.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
                await connection.NotifyAsync(BitcoinStratumMethods.MiningNotify, currentJobParams);
            }
        }

        #endregion // Overrides
    }
}
