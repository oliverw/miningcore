using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningCore.JsonRpc;
using MiningCore.Mining;
using MiningCore.Persistence;
using MiningCore.Persistence.Repositories;
using MiningCore.Stratum;
using Newtonsoft.Json;
using NLog;

namespace MiningCore.Blockchain.Bitcoin
{
    [BitcoinCoinsMetaData]
    public class BitcoinPool : PoolBase<BitcoinWorkerContext>
    {
        public BitcoinPool(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper) :
            base(ctx, serializerSettings, cf, statsRepo, mapper)
        {
        }

        private object currentJobParams;
        private BitcoinJobManager manager;
        private static readonly TimeSpan maxShareAge = TimeSpan.FromSeconds(5);

        private void OnSubscribe(StratumClient<BitcoinWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
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
                        new object[] {BitcoinStratumMethods.SetDifficulty, client.ConnectionId},
                        new object[] {BitcoinStratumMethods.MiningNotify, client.ConnectionId}
                    }
                }
                .Concat(manager.GetSubscriberData(client))
                .ToArray();

            client.Respond(data, request.Id);

            // setup worker context
            client.Context.IsSubscribed = true;
            client.Context.UserAgent = requestParams?.Length > 0 ? requestParams[0].Trim() : null;

            // send intial update
            client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] {client.Context.Difficulty});
            client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
        }

        private async Task OnAuthorizeAsync(StratumClient<BitcoinWorkerContext> client,
            Timestamped<JsonRpcRequest> tsRequest)
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

        private async Task OnSubmitAsync(StratumClient<BitcoinWorkerContext> client,
            Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            // check age of submission (aged submissions are usually caused by high server load)
            var requestAge = DateTime.UtcNow - tsRequest.Timestamp;

            if (requestAge > maxShareAge)
            {
                logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Dropping stale share submission request (not client's fault)");
                return;
            }

            // check worker state
            client.Context.LastActivity = DateTime.UtcNow;

            if (!client.Context.IsAuthorized)
            {
                client.RespondError(StratumError.UnauthorizedWorker, "Unauthorized worker", request.Id);
            }
            else if (!client.Context.IsSubscribed)
            {
                client.RespondError(StratumError.NotSubscribed, "Not subscribed", request.Id);
            }
            else
            {
                UpdateVarDiff(client, manager.BlockchainStats.NetworkDifficulty);

                try
                {
                    // submit 
                    var requestParams = request.ParamsAs<string[]>();
                    var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                    var share = await manager.SubmitShareAsync(client, requestParams, client.Context.Difficulty,
                        poolEndpoint.Difficulty);

                    // success
                    client.Respond(true, request.Id);

                    // record it
                    shareSubject.OnNext(share);

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

                    // banning
                    if (poolConfig.Banning?.Enabled == true)
                        ConsiderBan(client, client.Context, poolConfig.Banning);
                }
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

            catch (Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Enable to convert suggested difficulty {request.Params}");
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
                    var lastActivityAgo = DateTime.UtcNow - client.Context.LastActivity;

                    if (poolConfig.ClientConnectionTimeout > 0 &&
                        lastActivityAgo.TotalSeconds > poolConfig.ClientConnectionTimeout)
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (idle-timeout exceeded)");
                        DisconnectClient(client);
                        return;
                    }

                    // varDiff: if the client has a pending difficulty change, apply it now
                    if (client.Context.ApplyPendingDifficulty())
                        client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] {client.Context.Difficulty});

                    // send job
                    client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
                }
            });
        }

        #region Overrides

        protected override async Task SetupJobManager()
        {
            manager = ctx.Resolve<BitcoinJobManager>();
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

            switch (request.Method)
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

                default:
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        protected override void UpdateVarDiff(StratumClient<BitcoinWorkerContext> client)
        {
            UpdateVarDiff(client, manager.BlockchainStats.NetworkDifficulty);

            if (client.Context.ApplyPendingDifficulty())
            {
                client.Notify(BitcoinStratumMethods.SetDifficulty, new object[] { client.Context.Difficulty });
            }
        }

        protected override void UpdateBlockChainStats()
        {
            blockchainStats = manager.BlockchainStats;
        }

        #endregion // Overrides
    }
}
