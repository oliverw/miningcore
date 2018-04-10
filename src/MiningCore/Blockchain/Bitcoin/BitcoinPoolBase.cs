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
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningCore.Blockchain.Bitcoin.DaemonResponses;
using MiningCore.Configuration;
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
    public class BitcoinPoolBase<TJob, TBlockTemplate> : PoolBase
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

        protected virtual void OnSubscribe(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var context = client.GetContextAs<BitcoinWorkerContext>();
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
            context.IsSubscribed = true;
            context.UserAgent = requestParams?.Length > 0 ? requestParams[0].Trim() : null;

            // send intial update
            client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
            client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
        }

        protected virtual async Task OnAuthorizeAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var context = client.GetContextAs<BitcoinWorkerContext>();
            var requestParams = request.ParamsAs<string[]>();
            var workerValue = requestParams?.Length > 0 ? requestParams[0] : null;
            //var password = requestParams?.Length > 1 ? requestParams[1] : null;

            // extract worker/miner
            var split = workerValue?.Split('.');
            var minerName = split?.FirstOrDefault()?.Trim();
            var workerName = split?.Skip(1).FirstOrDefault()?.Trim() ?? string.Empty;

            // assumes that workerName is an address
            context.IsAuthorized = !string.IsNullOrEmpty(minerName) && await manager.ValidateAddressAsync(minerName);
            context.MinerName = minerName;
            context.WorkerName = workerName;

            // respond
            client.Respond(context.IsAuthorized, request.Id);

            // log association
            logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] = {workerValue} = {client.RemoteEndpoint.Address}");
        }

        protected virtual async Task OnSubmitAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<BitcoinWorkerContext>();

            try
            {
                if (request.Id == null)
                    throw new StratumException(StratumError.MinusOne, "missing request id");

                // check age of submission (aged submissions are usually caused by high server load)
                var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

                if (requestAge > maxShareAge)
                {
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Dropping stale share submission request (not client's fault)");
                    return;
                }

                // check worker state
                context.LastActivity = clock.Now;

                // validate worker
                if (!context.IsAuthorized)
                    throw new StratumException(StratumError.UnauthorizedWorker, "Unauthorized worker");
                else if (!context.IsSubscribed)
                    throw new StratumException(StratumError.NotSubscribed, "Not subscribed");

                // submit
                var requestParams = request.ParamsAs<string[]>();
                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                var share = await manager.SubmitShareAsync(client, requestParams, poolEndpoint.Difficulty);

                // success
                client.Respond(true, request.Id);
                shareSubject.OnNext(new ClientShare(client, share));

                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty, 3)}");

                // update pool stats
                if (share.IsBlockCandidate)
                    poolStats.LastPoolBlockTime = clock.Now;

                // update client stats
                context.Stats.ValidShares++;
                UpdateVarDiff(client);
            }

            catch (StratumException ex)
            {
                client.RespondError(ex.Code, ex.Message, request.Id, false);

                // update client stats
                context.Stats.InvalidShares++;
                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Share rejected: {ex.Code}");

                // banning
                ConsiderBan(client, context, poolConfig.Banning);
            }
        }

        private void OnSuggestDifficulty(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<BitcoinWorkerContext>();

            // acknowledge
            client.Respond(true, request.Id);

            try
            {
                var requestedDiff = (double) Convert.ChangeType(request.Params, TypeCode.Double);

                // client may suggest higher-than-base difficulty, but not a lower one
                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                if (requestedDiff > poolEndpoint.Difficulty)
                {
                    context.SetDifficulty(requestedDiff);
                    client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });

                    logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Difficulty set to {requestedDiff} as requested by miner");
                }
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Unable to convert suggested difficulty {request.Params}");
            }
        }

        protected void OnGetTransactions(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
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

            logger.Info(() => $"[{LogCat}] Broadcasting job");

            ForEachClient(client =>
            {
                var context = client.GetContextAs<BitcoinWorkerContext>();

                if (context.IsSubscribed && context.IsAuthorized)
                {
                    // check alive
                    var lastActivityAgo = clock.Now - context.LastActivity;

                    if (poolConfig.ClientConnectionTimeout > 0 &&
                        lastActivityAgo.TotalSeconds > poolConfig.ClientConnectionTimeout)
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (idle-timeout exceeded)");
                        DisconnectClient(client);
                        return;
                    }

                    // varDiff: if the client has a pending difficulty change, apply it now
                    if (context.ApplyPendingDifficulty())
                        client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });

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

            if (poolConfig.EnableInternalStratum == true)
	        {
		        disposables.Add(manager.Jobs.Subscribe(OnNewJob));

		        // we need work before opening the gates
		        await manager.Jobs.Take(1).ToTask();
	        }
        }

        protected override void InitStats()
        {
            base.InitStats();

            blockchainStats = manager.BlockchainStats;
        }

        protected override WorkerContextBase CreateClientContext()
        {
            return new BitcoinWorkerContext();
        }

        protected override async Task OnRequestAsync(StratumClient client,
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
                    //OnGetTransactions(client, tsRequest);
                    // ignored
                    break;

                case BitcoinStratumMethods.ExtraNonceSubscribe:
                    // ignored
                    break;

                case BitcoinStratumMethods.MiningMultiVersion:
                    // ignored
                    break;
                
                default:
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        public override double HashrateFromShares(double shares, double interval)
        {
            var multiplier = BitcoinConstants.Pow2x32 / manager.ShareMultiplier;
            var result = shares * multiplier / interval;

            // OW: tmp hotfix
            if (poolConfig.Coin.Type == CoinType.MONA || poolConfig.Coin.Type == CoinType.VTC ||
                poolConfig.Coin.Type == CoinType.STAK ||
                (poolConfig.Coin.Type == CoinType.XVG && poolConfig.Coin.Algorithm.ToLower() == "lyra"))
                result *= 4;

          return result;
        }

        protected override void OnVarDiffUpdate(StratumClient client, double newDiff)
        {
            var context = client.GetContextAs<BitcoinWorkerContext>();
            context.EnqueueNewDifficulty(newDiff);

            // apply immediately and notify client
            if (context.HasPendingDifficulty)
            {
                context.ApplyPendingDifficulty();

                client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { context.Difficulty });
                client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
            }
        }

        #endregion // Overrides
    }
}
