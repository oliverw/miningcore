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
using Autofac.Features.Metadata;
using AutoMapper;
using MiningCore.Configuration;
using MiningCore.JsonRpc;
using MiningCore.Mining;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Repositories;
using MiningCore.Stratum;
using Newtonsoft.Json;

namespace MiningCore.Blockchain.Ethereum
{
    [CoinMetadata(CoinType.ETH, CoinType.ETC)]
    public class EthereumPool : PoolBase<EthereumWorkerContext>
    {
        public EthereumPool(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IEnumerable<Meta<INotificationSender, NotificationSenderMetadataAttribute>> notificationSenders) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, notificationSenders)
        {
        }

        private long currentJobId;

        private EthereumJobManager manager;
        private static readonly TimeSpan maxShareAge = TimeSpan.FromSeconds(5);

        private string NextJobId()
        {
            return Interlocked.Increment(ref currentJobId).ToString(CultureInfo.InvariantCulture);
        }

        private void OnLogin(StratumClient<EthereumWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
            {
                client.RespondError(StratumError.MinusOne, "missing request id", request.Id);
                return;
            }

            var loginRequest = request.ParamsAs<string[]>();

            if(loginRequest == null || loginRequest.Length == 0 || string.IsNullOrEmpty(loginRequest[0]))
            {
                client.RespondError(StratumError.MinusOne, "missing login", request.Id);
                return;
            }

            // extract worker/miner/paymentid
            var split = loginRequest[0].Split('.');
            client.Context.MinerName = split[0];
            client.Context.WorkerName = split.Length > 1 ? split[1] : null;

            // validate login
            var result = manager.ValidateAddress(client.Context.MinerName);

            client.Context.IsSubscribed = result;
            client.Context.IsAuthorized = result;

            if (!client.Context.IsAuthorized)
            {
                client.RespondError(StratumError.MinusOne, "missing login", request.Id);
                return;
            }

            // respond
            client.Respond(true, request.Id);
        }

        private void OnGetJob(StratumClient<EthereumWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
            {
                client.RespondError(StratumError.MinusOne, "missing request id", request.Id);
                return;
            }

            // validate worker
            if (!client.Context.IsAuthorized)
            {
                client.RespondError(StratumError.MinusOne, "unauthorized", request.Id);
                return;
            }

            // respond
            var jobParams = manager.GetJobParamsForStratum(client);
            client.Respond(jobParams, request.Id);
        }

        private async Task OnSubmitAsync(StratumClient<EthereumWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
            {
                client.RespondError(StratumError.MinusOne, "missing request id", request.Id);
                return;
            }

            // check age of submission (aged submissions are usually caused by high server load)
            var requestAge = DateTime.UtcNow - tsRequest.Timestamp.UtcDateTime;

            if (requestAge > maxShareAge)
            {
                logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Dropping stale share submission request (not client's fault)");
                return;
            }

            // validate worker
            if (!client.Context.IsAuthorized)
                throw new StratumException(StratumError.MinusOne, "unauthorized");

            // check request
            var submitRequest = request.ParamsAs<string[]>();

            if (submitRequest.Length != 3 || 
                submitRequest.Any(x=> string.IsNullOrEmpty(x)) || 
                !EthereumConstants.NoncePattern.IsMatch(submitRequest[0]) ||
                !EthereumConstants.HashPattern.IsMatch(submitRequest[1]) ||
                !EthereumConstants.HashPattern.IsMatch(submitRequest[2]))
                throw new StratumException(StratumError.MinusOne, "malformed PoW result");

            // recognize activity
            client.Context.LastActivity = DateTime.UtcNow;

            try
            {
                RegisterShareSubmission(client);

                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                var share = await manager.SubmitShareAsync(client, submitRequest, client.Context.Difficulty, poolEndpoint.Difficulty);

                // success
                client.Respond(true, request.Id);

                // record it
                shareSubject.OnNext(share);

                logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Share accepted: D{share.StratumDifficulty}");

                // update pool stats
                if (share.IsBlockCandidate)
                    poolStats.LastPoolBlockTime = DateTime.UtcNow;

                // update client stats
                client.Context.Stats.ValidShares++;

                // telemetry
                validSharesSubject.OnNext(share);
            }

            catch (StratumException ex)
            {
                client.RespondError(ex.Code, ex.Message, request.Id, false);

                // update client stats
                client.Context.Stats.InvalidShares++;

                // telemetry
                invalidSharesSubject.OnNext(Unit.Default);

                logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Share rejected: {ex.Code}");

                // banning
                if (poolConfig.Banning?.Enabled == true)
                    ConsiderBan(client, client.Context, poolConfig.Banning);
            }
        }

        private void OnNewJob()
        {
            ForEachClient(client =>
            {
                if (client.Context.IsSubscribed)
                {
                    // check alive
                    var lastActivityAgo = DateTime.UtcNow - client.Context.LastActivity;

                    if (poolConfig.ClientConnectionTimeout > 0 &&
                        lastActivityAgo.TotalSeconds > poolConfig.ClientConnectionTimeout)
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (idle-timeout exceeded)");
                        DisconnectClient(client);
                        return;
                    }

                    // send job
                    var jobParams = manager.GetJobParamsForStratum(client);
                    client.Notify(new JsonRpcRequest(null, jobParams, 0));
                }
            });
        }

        #region Overrides

        protected override async Task SetupJobManager()
        {
            manager = ctx.Resolve<EthereumJobManager>();
            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync();

            disposables.Add(manager.Blocks.Subscribe(_ => OnNewJob()));

            // we need work before opening the gates
            await manager.Blocks.Take(1).ToTask();
        }

        protected override async Task OnRequestAsync(StratumClient<EthereumWorkerContext> client,
            Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            switch (request.Method)
            {
                case EthereumStratumMethods.Login:
                    OnLogin(client, tsRequest);
                    break;

                case EthereumStratumMethods.GetWork:
                    OnGetJob(client, tsRequest);
                    break;

                case EthereumStratumMethods.Submit:
                    await OnSubmitAsync(client, tsRequest);
                    break;

                case EthereumStratumMethods.SubmitHashrate:
                    // recognize activity
                    client.Context.LastActivity = DateTime.UtcNow;
                    client.Respond("", request.Id);
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

            disposables.Add(validSharesSubject
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

                    catch (Exception ex)
                    {
                        logger.Error(ex);
                        return 0ul;
                    }
                })
                .Subscribe(hashRate => poolStats.PoolHashRate = hashRate));
        }

        protected override ulong HashrateFromShares(IEnumerable<IShare> shares, int interval)
        {
            var result = Math.Ceiling(shares.Sum(share => share.StratumDifficulty) / interval);
            return (ulong)result;
        }

        protected override void OnVarDiffUpdate(StratumClient<EthereumWorkerContext> client, double newDiffValue)
        {
            base.OnVarDiffUpdate(client, newDiffValue);

            // apply immediately and notify client
            if (client.Context.HasPendingDifficulty)
            {
                client.Context.ApplyPendingDifficulty();

                // send job
                var jobParams = manager.GetJobParamsForStratum(client);
                client.Notify(new JsonRpcRequest(null, jobParams, 0));
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
