using System;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using Microsoft.Extensions.Logging;
using MiningCore.Authorization;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Configuration.Extensions;
using MiningCore.JsonRpc;
using MiningCore.Stratum;
using Newtonsoft.Json;

namespace MiningCore.MiningPool
{
    public class Pool
    {
        public Pool(IComponentContext ctx, ILogger<Pool> logger, JsonSerializerSettings serializerSettings)
        {
            this.ctx = ctx;
            this.logger = logger;
            this.serializerSettings = serializerSettings;
        }

        private readonly IComponentContext ctx;
        private readonly ILogger<Pool> logger;
        private StratumServer server;
        private IStratumAuthorizer authorizer;
        private IBlockchainDemon daemon;
        private readonly NetworkStats networkStats = new NetworkStats();
        private readonly PoolStats poolStats = new PoolStats();
        private readonly JsonSerializerSettings serializerSettings;

        #region API-Surface

        public async Task InitAsync(PoolConfig poolConfig, PoolClusterConfig poolClusterConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(poolClusterConfig, nameof(poolClusterConfig));

            try
            {
                logger.Info(() => $"{poolConfig.Coin.Name} initializing ...");

                await InitializeDaemon(poolConfig);
                InitializeStratum(poolConfig);
            }

            catch (Exception ex)
            {
                logger.Error(() => $"Error during pool startup. Pool cannot start", ex);
            }
        }

        private async Task InitializeDaemon(PoolConfig poolConfig)
        {
            daemon = ctx.ResolveNamed<IBlockchainDemon>(poolConfig.Coin.Family.ToString());
            await daemon.InitAsync(poolConfig);

            while (!(await daemon.IsHealthyAsync()))
            { 
                logger.Info(()=> $"Waiting for coin-daemons to come online ...");

                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            logger.Info(() => $"All coin-daemons are online");
        }

        public NetworkStats NetworkStats => networkStats;
        public PoolStats PoolStats => poolStats;

        #endregion // API-Surface

        private void InitializeStratum(PoolConfig poolConfig)
        {
            // resolve authorizer
            authorizer = ctx.ResolveNamed<IStratumAuthorizer>(poolConfig.Authorizer.ToString());

            // start server
            server = ctx.Resolve<StratumServer>();
            server.Init(poolConfig);

            // monitor connecting clients
            server.ClientConnected.Subscribe(OnClientConnected);
            server.ClientDisconnected.Subscribe(OnClientDisconnected);
        }

        private void OnClientConnected(StratumClient client)
        {
            try
            {
                // monitor client requests
                client.Requests
                    .ObserveOn(TaskPoolScheduler.Default)
                    .Subscribe(x => OnClientRpcRequest(client, x), ex => OnClientReceiveError(client, ex), () => OnClientReceiveComplete(client));

                // expect miner to establish communicatin within a certain time
                EnsureNoZombieClient(client);

                // update stats
                poolStats.ConnectedMiners = server.ClientCount;
            }

            catch (Exception ex)
            {
                logger.Error(()=> "OnClientConnected", ex);
            }
        }

        private void OnClientDisconnected(string subscriptionId)
        {
            try
            {
                // update stats
                poolStats.ConnectedMiners = server.ClientCount;
            }

            catch (Exception ex)
            {
                logger.Error(() => "OnClientConnected", ex);
            }
        }

        private void OnClientReceiveError(StratumClient client, Exception ex)
        {
            logger.Error(() => $"[{client.SubscriptionId}] Client connection entered error state: {ex.Message}");

            server.DisconnectClient(client);
        }

        private void OnClientReceiveComplete(StratumClient client)
        {
            logger.Debug(() => $"[{client.SubscriptionId}] Received End-of-Stream from client");

            server.DisconnectClient(client);
        }

        private void OnClientRpcRequest(StratumClient client, JsonRpcRequest request)
        {
            logger.Debug(() => $"[{client.SubscriptionId}] Received request {request.Method} [{request.Id}]: {JsonConvert.SerializeObject(request.Params, serializerSettings)}");

            try
            {
                switch (request.Method)
                {
                    case StratumConstants.MsgSubscribe:
                        OnClientSubscribe(client, request);
                        break;
                    case StratumConstants.MsgAuthorize:
                        OnClientAuthorize(client, request);
                        break;
                    case StratumConstants.MsgSubmitShare:
                        OnClientSubmitShare(client, request);
                        break;

                    default:
                        logger.Warning(() => $"[{client.SubscriptionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                        client.SendError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                        break;
                }
            }

            catch (Exception ex)
            {
                logger.Error(() => $"OnClientRpcRequest: {request.Method}", ex);
            }
        }

        private void OnClientSubscribe(StratumClient client, JsonRpcRequest request)
        {
            // [
            //    [
            //        ["mining.set_difficulty", "b4b6693b72a50c7116db18d6497cac52"],
            //        ["mining.notify", "ae6812eb4cd7735a302a8a9dd95cf71f"]
            //    ], 
            //    "08000002", 
            //    4
            // ];
        }

        private async void OnClientAuthorize(StratumClient client, JsonRpcRequest request)
        {
            var requestParams = request.Params?.ToObject<string[]>();
            var address = requestParams?.Length > 0 ? requestParams[0] : null;

            if (!string.IsNullOrEmpty(address))
            {
                client.IsAuthorized = await daemon.ValidateAddressAsync(address);
                client.Send(client.IsAuthorized, request.Id);
            }

            else
                client.SendError(StratumError.Other, "invalid or missing workername", request.Id);
        }

        private void OnClientSubmitShare(StratumClient client, JsonRpcRequest request)
        {
            lock (client)
            {
                client.LastActivity = DateTime.UtcNow;

                if (!client.IsAuthorized)
                    client.SendError(StratumError.UnauthorizedWorker, "Unauthorized worker", request.Id);
                else
                {
                    // TODO
                }
            }
        }

        private void EnsureNoZombieClient(StratumClient client)
        {
            var isAlive = client.Requests
                .Take(1)
                .Select(_ => true);

            var timeout = Observable.Timer(DateTime.UtcNow.AddSeconds(10))
                .Select(_ => false);

            Observable.Merge(isAlive, timeout)
                .Take(1)
                .Subscribe(alive =>
                {
                    if (!alive)
                    {
                        logger.Debug(() => $"[{client.SubscriptionId}] Booting miner because it failed to establish communication within the alloted time");

                        server.DisconnectClient(client);
                    }
                });
        }
    }
}
