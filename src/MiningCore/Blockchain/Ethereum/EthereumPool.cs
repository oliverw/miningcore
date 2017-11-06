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
using MiningCore.Blockchain.Ethereum.Configuration;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.JsonRpc;
using MiningCore.Mining;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Repositories;
using MiningCore.Stratum;
using MiningCore.Time;
using MiningCore.Util;
using Newtonsoft.Json;

namespace MiningCore.Blockchain.Ethereum
{
    [CoinMetadata(CoinType.ETH, CoinType.ETC, CoinType.EXP)]
    public class EthereumPool : PoolBase<EthereumWorkerContext>
    {
        public EthereumPool(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            NotificationService notificationService) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, notificationService)
        {
        }

        private object currentJobParams;
        private EthereumJobManager manager;

        private void OnSubscribe(StratumClient<EthereumWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var requestParams = request.ParamsAs<string[]>();

            if (requestParams == null || requestParams.Length < 2 || requestParams.Any(string.IsNullOrEmpty))
            {
                client.RespondError(StratumError.MinusOne, "invalid request", request.Id);
                return;
            }

            manager.PrepareWorker(client);

            var data = new object[]
                {
                    new object[]
                    {
                        EthereumStratumMethods.MiningNotify,
                        client.ConnectionId,
                        EthereumConstants.EthereumStratumVersion
                    },
                    client.Context.ExtraNonce1
                }
                .ToArray();

            client.Respond(data, request.Id);

            // setup worker context
            client.Context.IsSubscribed = true;
            client.Context.UserAgent = requestParams[0].Trim();
        }

        private void OnAuthorize(StratumClient<EthereumWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
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
            var workerName = split?.LastOrDefault();

            // assumes that workerName is an address
            client.Context.IsAuthorized = manager.ValidateAddress(minerName);
            client.Context.MinerName = minerName;
            client.Context.WorkerName = workerName;
            client.Respond(client.Context.IsAuthorized, request.Id);

            // send intial update
            client.Notify(EthereumStratumMethods.SetDifficulty, new object[] { client.Context.Difficulty });
            client.Notify(EthereumStratumMethods.MiningNotify, currentJobParams);
        }

        private async Task OnSubmitAsync(StratumClient<EthereumWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
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

                // validate worker
                if (!client.Context.IsAuthorized)
                    throw new StratumException(StratumError.UnauthorizedWorker, "Unauthorized worker");
                else if (!client.Context.IsSubscribed)
                    throw new StratumException(StratumError.NotSubscribed, "Not subscribed");

                // check request
                var submitRequest = request.ParamsAs<string[]>();

                if (submitRequest.Length != 3 ||
                    submitRequest.Any(string.IsNullOrEmpty))
                    throw new StratumException(StratumError.MinusOne, "malformed PoW result");

                // recognize activity
                client.Context.LastActivity = clock.UtcNow;

                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                var share = await manager.SubmitShareAsync(client, submitRequest, client.Context.Difficulty,
                    poolEndpoint.Difficulty);

                // success
                client.Respond(true, request.Id);
                shareSubject.OnNext(Tuple.Create((object) client, share));

                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty, 3)}");

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

        private void OnNewJob(object jobParams)
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
                        client.Notify(EthereumStratumMethods.SetDifficulty, new object[] { client.Context.Difficulty });

                    // send job
                    client.Notify(EthereumStratumMethods.MiningNotify, currentJobParams);
                }
            });
        }

        #region Overrides

        protected override async Task SetupJobManager()
        {
            manager = ctx.Resolve<EthereumJobManager>();
            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync();

            disposables.Add(manager.Jobs.Subscribe(OnNewJob));

            // we need work before opening the gates
            await manager.Jobs.Take(1).ToTask();
        }

        protected override async Task OnRequestAsync(StratumClient<EthereumWorkerContext> client,
            Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            switch(request.Method)
            {
                case EthereumStratumMethods.Subscribe:
                    OnSubscribe(client, tsRequest);
                    break;

                case EthereumStratumMethods.Authorize:
                    OnAuthorize(client, tsRequest);
                    break;

                case EthereumStratumMethods.SubmitShare:
                    await OnSubmitAsync(client, tsRequest);
                    break;

                case EthereumStratumMethods.ExtraNonceSubscribe:
                    client.Respond(true, request.Id);
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
            var result = Math.Ceiling(shares.Sum(share => share.Item2.Difficulty) / interval);
            return (ulong) result;
        }

        protected override void OnVarDiffUpdate(StratumClient<EthereumWorkerContext> client, double newDiff)
        {
            base.OnVarDiffUpdate(client, newDiff);

            // apply immediately and notify client
            if (client.Context.HasPendingDifficulty)
            {
                client.Context.ApplyPendingDifficulty();

                // send job
                client.Notify(EthereumStratumMethods.SetDifficulty, new object[] { client.Context.Difficulty });
                client.Notify(EthereumStratumMethods.MiningNotify, currentJobParams);
            }
        }

        protected override async Task UpdateBlockChainStatsAsync()
        {
            await manager.UpdateNetworkStatsAsync();

            blockchainStats = manager.BlockchainStats;
        }

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            base.Configure(poolConfig, clusterConfig);

            // validate mandatory extra config
            var extraConfig = poolConfig.PaymentProcessing.Extra.SafeExtensionDataAs<EthereumPoolPaymentProcessingConfigExtra>();
            if (string.IsNullOrEmpty(extraConfig?.CoinbasePassword))
                logger.ThrowLogPoolStartupException("\"paymentProcessing.coinbasePassword\" pool-configuration property missing or empty (required for unlocking wallet during payment processing)");
        }

        #endregion // Overrides
    }
}
