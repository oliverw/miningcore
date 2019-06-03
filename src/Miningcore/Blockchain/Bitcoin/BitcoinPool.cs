/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

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
            IMessageBus messageBus) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus)
        {
        }

        protected object currentJobParams;
        protected BitcoinJobManager manager;
        private BitcoinTemplate coin;

        protected virtual async Task OnSubscribeAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            var context = client.ContextAs<BitcoinWorkerContext>();
            var requestParams = request.ParamsAs<string[]>();

            var data = new object[]
                {
                    new object[]
                    {
                        new object[] { BitcoinStratumMethods.SetDifficulty, client.ConnectionId },
                        new object[] { BitcoinStratumMethods.MiningNotify, client.ConnectionId }
                    }
                }
                .Concat(manager.GetSubscriberData(client))
                .ToArray();

            await client.RespondAsync(data, request.Id);

            // setup worker context
            context.IsSubscribed = true;
            context.UserAgent = requestParams?.Length > 0 ? requestParams[0].Trim() : null;

            // send intial update
            await client.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
            await client.NotifyAsync(BitcoinStratumMethods.MiningNotify, currentJobParams);
        }

        protected virtual async Task OnAuthorizeAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;

            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            var context = client.ContextAs<BitcoinWorkerContext>();
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
                await client.RespondAsync(context.IsAuthorized, request.Id);

                // log association
                logger.Info(() => $"[{client.ConnectionId}] Authorized worker {workerValue}");

                // extract control vars from password
                var staticDiff = GetStaticDiffFromPassparts(passParts);
                if(staticDiff.HasValue &&
                    (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                        context.VarDiff == null && staticDiff.Value > context.Difficulty))
                {
                    context.VarDiff = null; // disable vardiff
                    context.SetDifficulty(staticDiff.Value);

                    logger.Info(() => $"[{client.ConnectionId}] Setting static difficulty of {staticDiff.Value}");

                    await client.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
                }
            }

            else
            {
                // respond
                await client.RespondErrorAsync(StratumError.UnauthorizedWorker, "Authorization failed", request.Id, context.IsAuthorized);

                // issue short-time ban if unauthorized to prevent DDos on daemon (validateaddress RPC)
                logger.Info(() => $"[{client.ConnectionId}] Banning unauthorized worker for 60 sec");

                banManager.Ban(client.RemoteEndpoint.Address, TimeSpan.FromSeconds(60));

                DisconnectClient(client);
            }
        }

        protected virtual async Task OnSubmitAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;
            var context = client.ContextAs<BitcoinWorkerContext>();

            try
            {
                if(request.Id == null)
                    throw new StratumException(StratumError.MinusOne, "missing request id");

                // check age of submission (aged submissions are usually caused by high server load)
                var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

                if(requestAge > maxShareAge)
                {
                    logger.Warn(() => $"[{client.ConnectionId}] Dropping stale share submission request (server overloaded?)");
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
                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                var share = await manager.SubmitShareAsync(client, requestParams, poolEndpoint.Difficulty, ct);

                await client.RespondAsync(true, request.Id);

                // publish
                messageBus.SendMessage(new ClientShare(client, share));

                // telemetry
                PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

                logger.Info(() => $"[{client.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty * coin.ShareMultiplier, 3)}");

                // update pool stats
                if(share.IsBlockCandidate)
                    poolStats.LastPoolBlockTime = clock.Now;

                // update client stats
                context.Stats.ValidShares++;
                await UpdateVarDiffAsync(client);
            }

            catch(StratumException ex)
            {
                // telemetry
                PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, false);

                // update client stats
                context.Stats.InvalidShares++;
                logger.Info(() => $"[{client.ConnectionId}] Share rejected: {ex.Message}");

                // banning
                ConsiderBan(client, context, poolConfig.Banning);

                throw;
            }
        }

        private async Task OnSuggestDifficultyAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.ContextAs<BitcoinWorkerContext>();

            // acknowledge
            await client.RespondAsync(true, request.Id);

            try
            {
                var requestedDiff = (double) Convert.ChangeType(request.Params, TypeCode.Double);

                // client may suggest higher-than-base difficulty, but not a lower one
                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                if(requestedDiff > poolEndpoint.Difficulty)
                {
                    context.SetDifficulty(requestedDiff);
                    await client.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });

                    logger.Info(() => $"[{client.ConnectionId}] Difficulty set to {requestedDiff} as requested by miner");
                }
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"Unable to convert suggested difficulty {request.Params}");
            }
        }

        private async Task OnConfigureMiningAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.ContextAs<BitcoinWorkerContext>();

            var requestParams = request.ParamsAs<JToken[]>();
            var extensions = requestParams[0].ToObject<string[]>();
            var extensionParams = requestParams[1].ToObject<Dictionary<string, JToken>>();
            var result = new Dictionary<string, object>();

            foreach(var extension in extensions)
            {
                switch(extension)
                {
                    case BitcoinStratumExtensions.VersionRolling:
                        ConfigureVersionRolling(client, context, extensionParams, result);
                        break;

                    case BitcoinStratumExtensions.MinimumDiff:
                        ConfigureMinimumDiff(client, context, extensionParams, result);
                        break;
                }
            }

            await client.RespondAsync(result, request.Id);
        }

        private void ConfigureVersionRolling(StratumClient client, BitcoinWorkerContext context,
            Dictionary<string, JToken> extensionParams, Dictionary<string, object> result)
        {
            //var requestedBits = extensionParams[BitcoinStratumExtensions.VersionRollingBits].Value<int>();
            uint requestedMask = BitcoinConstants.VersionRollingPoolMask;

            if(extensionParams.TryGetValue(BitcoinStratumExtensions.VersionRollingMask, out var requestedMaskValue))
                requestedMask = uint.Parse(requestedMaskValue.Value<string>(), NumberStyles.HexNumber);

            // Compute effective mask
            context.VersionRollingMask = BitcoinConstants.VersionRollingPoolMask & requestedMask;

            // enabled
            result[BitcoinStratumExtensions.VersionRolling] = true;
            result[BitcoinStratumExtensions.VersionRollingMask] = context.VersionRollingMask.Value.ToStringHex8();

            logger.Info(() => $"[{client.ConnectionId}] Using version-rolling mask {result[BitcoinStratumExtensions.VersionRollingMask]}");
        }

        private void ConfigureMinimumDiff(StratumClient client, BitcoinWorkerContext context,
            Dictionary<string, JToken> extensionParams, Dictionary<string, object> result)
        {
            var requestedDiff = extensionParams[BitcoinStratumExtensions.MinimumDiffValue].Value<double>();

            // client may suggest higher-than-base difficulty, but not a lower one
            var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

            if(requestedDiff > poolEndpoint.Difficulty)
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(requestedDiff);

                logger.Info(() => $"[{client.ConnectionId}] Difficulty set to {requestedDiff} as requested by miner. VarDiff now disabled.");

                // enabled
                result[BitcoinStratumExtensions.MinimumDiff] = true;
            }
        }

        protected virtual async Task OnNewJobAsync(object jobParams)
        {
            currentJobParams = jobParams;

            logger.Info(() => $"Broadcasting job");

            var tasks = ForEachClient(async client =>
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
                        DisconnectClient(client);
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
                new TypedParameter(typeof(IExtraNonceProvider), new BitcoinExtraNonceProvider()));

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

        protected override async Task OnRequestAsync(StratumClient client,
            Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;

            try
            {
                switch(request.Method)
                {
                    case BitcoinStratumMethods.Subscribe:
                        await OnSubscribeAsync(client, tsRequest);
                        break;

                    case BitcoinStratumMethods.Authorize:
                        await OnAuthorizeAsync(client, tsRequest, ct);
                        break;

                    case BitcoinStratumMethods.SubmitShare:
                        await OnSubmitAsync(client, tsRequest, ct);
                        break;

                    case BitcoinStratumMethods.SuggestDifficulty:
                        await OnSuggestDifficultyAsync(client, tsRequest);
                        break;

                    case BitcoinStratumMethods.MiningConfigure:
                        await OnConfigureMiningAsync(client, tsRequest);
                        // ignored
                        break;

                    case BitcoinStratumMethods.ExtraNonceSubscribe:
                        await client.RespondAsync(true, request.Id);
                        break;

                    case BitcoinStratumMethods.GetTransactions:
                        // ignored
                        break;

                    case BitcoinStratumMethods.MiningMultiVersion:
                        // ignored
                        break;

                    default:
                        logger.Debug(() => $"[{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                        await client.RespondErrorAsync(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                        break;
                }
            }

            catch(StratumException ex)
            {
                await client.RespondErrorAsync(ex.Code, ex.Message, request.Id, false);
            }
        }

        protected override async Task OnVarDiffUpdateAsync(StratumClient client, double newDiff)
        {
            var context = client.ContextAs<BitcoinWorkerContext>();
            context.EnqueueNewDifficulty(newDiff);

            // apply immediately and notify client
            if(context.HasPendingDifficulty)
            {
                context.ApplyPendingDifficulty();

                await client.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
                await client.NotifyAsync(BitcoinStratumMethods.MiningNotify, currentJobParams);
            }
        }

        #endregion // Overrides
    }
}
