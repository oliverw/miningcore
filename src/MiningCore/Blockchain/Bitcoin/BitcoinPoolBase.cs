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
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningCore.Blockchain.Bitcoin.DaemonResponses;
using MiningCore.JsonRpc;
using MiningCore.Mining;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Repositories;
using MiningCore.Stratum;
using MiningCore.Time;
using Newtonsoft.Json;
using NLog;

namespace MiningCore.Blockchain.Bitcoin
{
    public class BitcoinPoolBase<TJob, TBlockTemplate> : PoolBase<BitcoinWorkerContext>
        where TBlockTemplate : BlockTemplate
        where TJob : BitcoinJob<TBlockTemplate>, new()
    {
        public BitcoinPoolBase(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            NotificationService notificationService) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, notificationService)
        {
        }

        protected object currentJobParams;
        protected BitcoinJobManager<TJob, TBlockTemplate> manager;

        protected virtual void OnSubscribe(StratumClient<BitcoinWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

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

            client.Respond(data, request.Id);

            // setup worker context
            client.Context.IsSubscribed = true;
            client.Context.UserAgent = requestParams?.Length > 0 ? requestParams[0].Trim() : null;

            // send intial update
            client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { client.Context.Difficulty });
            client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
        }

        protected virtual async Task OnAuthorizeAsync(StratumClient<BitcoinWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var requestParams = request.ParamsAs<string[]>();
            var workerValue = requestParams?.Length > 0 ? requestParams[0] : null;
            //var password = requestParams?.Length > 1 ? requestParams[1] : null;

            // extract worker/miner
            var split = workerValue?.Split('.');
            var minerName = split?.FirstOrDefault();

            // assumes that workerName is an address
            client.Context.IsAuthorized = await manager.ValidateAddressAsync(minerName);
            client.Respond(client.Context.IsAuthorized, request.Id);
        }

        protected virtual async Task OnSubmitAsync(StratumClient<BitcoinWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            try
            {
                if (request.Id == null)
                    throw new StratumException(StratumError.MinusOne, "missing request id");

                // check age of submission (aged submissions are usually caused by high server load)
                var requestAge = clock.UtcNow - tsRequest.Timestamp.UtcDateTime;

                if (requestAge > maxShareAge)
                {
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Dropping stale share submission request (not client's fault)");
                    return;
                }

                // check worker state
                client.Context.LastActivity = clock.UtcNow;

                // validate worker
                if (!client.Context.IsAuthorized)
                    throw new StratumException(StratumError.UnauthorizedWorker, "Unauthorized worker");
                else if (!client.Context.IsSubscribed)
                    throw new StratumException(StratumError.NotSubscribed, "Not subscribed");

                // submit
                var requestParams = request.ParamsAs<string[]>();
                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                var share = await manager.SubmitShareAsync(client, requestParams, poolEndpoint.Difficulty);

                // success
                client.Respond(true, request.Id);
                shareSubject.OnNext(Tuple.Create((object) client, share));

                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty * manager.ShareMultiplier, 3)}");

                // update pool stats
                if (share.IsBlockCandidate)
                    poolStats.LastPoolBlockTime = clock.UtcNow;

                // update client stats
                client.Context.Stats.ValidShares++;
            }

            catch(StratumException ex)
            {
                client.RespondError(ex.Code, ex.Message, request.Id, false);

                // update client stats
                client.Context.Stats.InvalidShares++;
                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Share rejected: {ex.Code}");

                // banning
                if (poolConfig.Banning?.Enabled == true)
                    ConsiderBan(client, client.Context, poolConfig.Banning);
            }
        }

        private void OnSuggestDifficulty(StratumClient<BitcoinWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            // acknowledge
            client.Respond(true, request.Id);

            try
            {
                var requestedDiff = (double) Convert.ChangeType(request.Params, TypeCode.Double);

                // client may suggest higher-than-base difficulty, but not a lower one
                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                if (requestedDiff > poolEndpoint.Difficulty)
                {
                    client.Context.SetDifficulty(requestedDiff);
                    client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { client.Context.Difficulty });

                    logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Difficulty set to {requestedDiff} as requested by miner");
                }
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Unable to convert suggested difficulty {request.Params}");
            }
        }

        protected void OnGetTransactions(StratumClient<BitcoinWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            try
            {
                var transactions = manager.GetTransactions(client, request.ParamsAs<object[]>());

                client.Respond(transactions, request.Id);
            }

            catch(StratumException ex)
            {
                client.RespondError(ex.Code, ex.Message, request.Id, false);
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Unable to convert suggested difficulty {request.Params}");
            }
        }

        protected virtual void OnNewJob(object jobParams)
        {
            currentJobParams = jobParams;

            ForEachClient(client =>
            {
                if (client.Context.IsSubscribed)
                {
                    // check alive
                    var lastActivityAgo = clock.UtcNow - client.Context.LastActivity;

                    if (poolConfig.ClientConnectionTimeout > 0 &&
                        lastActivityAgo.TotalSeconds > poolConfig.ClientConnectionTimeout)
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (idle-timeout exceeded)");
                        DisconnectClient(client);
                        return;
                    }

                    // varDiff: if the client has a pending difficulty change, apply it now
                    if (client.Context.ApplyPendingDifficulty())
                        client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { client.Context.Difficulty });

                    // send job
                    client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
                }
            });
        }

        #region Overrides

        protected virtual BitcoinJobManager<TJob, TBlockTemplate> CreateJobManager()
        {
            return ctx.Resolve<BitcoinJobManager<TJob, TBlockTemplate>>(
                new TypedParameter(typeof(IExtraNonceProvider), new BitcoinExtraNonceProvider()));
        }

        protected override async Task SetupJobManager()
        {
            manager = CreateJobManager();
            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync();
            disposables.Add(manager.Jobs.Subscribe(OnNewJob));

            // we need work before opening the gates
            await manager.Jobs.Take(1).ToTask();
        }

        protected override async Task OnRequestAsync(StratumClient<BitcoinWorkerContext> client,
            Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            switch(request.Method)
            {
                case BitcoinStratumMethods.Subscribe:
                    OnSubscribe(client, tsRequest);
                    break;

                case BitcoinStratumMethods.Authorize:
                    await OnAuthorizeAsync(client, tsRequest);
                    break;

                case BitcoinStratumMethods.SubmitShare:
                    await OnSubmitAsync(client, tsRequest);
                    break;

                case BitcoinStratumMethods.SuggestDifficulty:
                    OnSuggestDifficulty(client, tsRequest);
                    break;

                case BitcoinStratumMethods.GetTransactions:
                    OnGetTransactions(client, tsRequest);
                    break;

                default:
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        protected override void SetupStats()
        {
            base.SetupStats();

            // Pool Hashrate
            var poolHashRateSampleIntervalSeconds = 60 * 10;

            disposables.Add(Shares
                .Buffer(TimeSpan.FromSeconds(poolHashRateSampleIntervalSeconds))
                .Do(shares => UpdateMinerHashrates(shares, poolHashRateSampleIntervalSeconds))
                .Select(shares =>
                {
                    if (!shares.Any())
                        return 0ul;

                    try
                    {
                        return HashrateFromShares(shares, poolHashRateSampleIntervalSeconds);
                    }

                    catch(Exception ex)
                    {
                        logger.Error(ex);
                        return 0ul;
                    }
                })
                .Subscribe(hashRate => poolStats.PoolHashRate = hashRate));
        }

        protected override ulong HashrateFromShares(IEnumerable<Tuple<object, IShare>> shares, int interval)
        {
            var sum = shares.Sum(share => Math.Max(0.00000001, share.Item2.Difficulty * manager.ShareMultiplier));
            var multiplier = BitcoinConstants.Pow2x32 / manager.ShareMultiplier;
            var result = Math.Ceiling(sum * multiplier / interval);
            return (ulong) result;
        }

        protected override void OnVarDiffUpdate(StratumClient<BitcoinWorkerContext> client, double newDiff)
        {
            client.Context.EnqueueNewDifficulty(newDiff);

            // apply immediately and notify client
            if (client.Context.HasPendingDifficulty)
            {
                client.Context.ApplyPendingDifficulty();

                client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { client.Context.Difficulty });
                client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
            }
        }

        protected override async Task UpdateBlockChainStatsAsync()
        {
            await manager.UpdateNetworkStatsAsync();

            blockchainStats = manager.BlockchainStats;
        }

        #endregion // Overrides
    }
}
