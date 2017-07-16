using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using LibUvManaged;
using Microsoft.Extensions.Logging;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Configuration.Extensions;
using MiningCore.JsonRpc;
using Newtonsoft.Json;

// TODO:
// - periodic job re-broadcast

namespace MiningCore.Stratum
{
    public class StratumServer
    {
        public StratumServer(IComponentContext ctx, ILogger<StratumServer> logger, 
            JsonSerializerSettings serializerSettings)
        {
            this.ctx = ctx;
            this.logger = logger;
            this.serializerSettings = serializerSettings;
        }

        private readonly IComponentContext ctx;
        private readonly ILogger<StratumServer> logger;
        private readonly Dictionary<int, LibUvListener> ports = new Dictionary<int, LibUvListener>();
        private readonly Dictionary<string, StratumClient> clients = new Dictionary<string, StratumClient>();
        private IBlockchainJobManager manager;
        private readonly NetworkStats networkStats = new NetworkStats();
        private readonly PoolStats poolStats = new PoolStats();
        private readonly JsonSerializerSettings serializerSettings;
        private object currentJobParams = null;
        private object currentJobParamsLock = new object();

        #region API-Surface

        public NetworkStats NetworkStats => networkStats;
        public PoolStats PoolStats => poolStats;

        public async Task StartAsync(PoolConfig poolConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
        
            try
            {
                logger.Info(() => $"{poolConfig.Coin.Name} starting ...");

                StartListeners(poolConfig);
                await InitializeJobManager(poolConfig);
            }

            catch (Exception ex)
            {
                logger.Error(() => $"Error during pool startup. Pool cannot start", ex);
            }
        }

        public void DisconnectClient(StratumClient client)
        {
            Contract.RequiresNonNull(client, nameof(client));

            var subscriptionId = client.SubscriptionId;
            client.Disconnect();

            if (!string.IsNullOrEmpty(subscriptionId))
            {
                lock (clients)
                {
                    clients.Remove(subscriptionId);
                }
            }
        }

        public void BroadcastNotification<T>(string method, T payload, Func<StratumClient, bool> filter = null)
        {
            BroadcastNotification(new JsonRpcRequest<T>(method, payload, null), filter);
        }

        public void BroadcastNotification<T>(JsonRpcRequest<T> notification, Func<StratumClient, bool> filter = null)
        {
            Contract.RequiresNonNull(notification, nameof(notification));

            StratumClient[] tmp;

            lock (clients)
            {
                tmp = clients.Values.ToArray();
            }

            foreach (var client in tmp)
            {
                if (filter != null && !filter(client))
                    continue;

                client.Notify(notification);
            }
        }

        #endregion // API-Surface

        private void StartListeners(PoolConfig poolConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

            // start ports
            foreach (var port in poolConfig.Ports.Keys)
            {
                var endpointConfig = poolConfig.Ports[port];

                // set listen addresse(s)
                var listenAddress = IPAddress.Parse("127.0.0.1");
                if (!string.IsNullOrEmpty(endpointConfig.ListenAddress))
                {
                    listenAddress = endpointConfig.ListenAddress != "*" ?
                        IPAddress.Parse(endpointConfig.ListenAddress) : IPAddress.Any;
                }

                // create endpoint
                var endPoint = new IPEndPoint(listenAddress, port);

                // create the listener
                var listener = new LibUvListener(ctx);

                lock (ports)
                {
                    ports[port] = listener;
                }

                // host it and its message loop in a dedicated background thread
                var task = new Task(() =>
                {
                    listener.Start(endPoint, con => OnClientConnected(con, endpointConfig));
                }, TaskCreationOptions.LongRunning);

                task.Start();

                logger.Info(() => $"{poolConfig.Coin.Name}: Stratum port {port} started");
            }
        }

        private async Task InitializeJobManager(PoolConfig poolConfig)
        {
            manager = ctx.ResolveNamed<IBlockchainJobManager>(poolConfig.Coin.Name.ToLower());
            await manager.StartAsync(poolConfig, this);

            manager.Jobs.Subscribe(OnNewJob);

            logger.Info(() => $"Job Manager started");
        }

        private void OnClientConnected(ILibUvConnection con, PoolEndpoint endpointConfig)
        {
            try
            {
                var subscriptionId = con.ConnectionId;

                var client = ctx.Resolve<StratumClient>(
                    new TypedParameter(typeof(PoolEndpoint), endpointConfig));

                lock (clients)
                {
                    clients[subscriptionId] = client;
                }

                client.Init(con, ctx);

                // monitor client requests
                client.Requests
                    .ObserveOn(TaskPoolScheduler.Default)
                    .Subscribe(x => OnClientRpcRequest(client, x), ex => OnClientReceiveError(client, ex), () => OnClientReceiveComplete(client));

                // expect miner to establish communication within a certain time
                EnsureNoZombieClient(client);

                // update stats
                lock (clients)
                {
                    poolStats.ConnectedMiners = clients.Count;
                }
            }

            catch (Exception ex)
            {
                logger.Error(() => "OnClientConnected", ex);
            }
        }

        private void OnClientReceiveError(StratumClient client, Exception ex)
        {
            logger.Error(() => $"[{client.SubscriptionId}] Client connection entered error state: {ex.Message}");

            DisconnectClient(client);
        }

        private void OnClientReceiveComplete(StratumClient client)
        {
            logger.Debug(() => $"[{client.SubscriptionId}] Received End-of-Stream from client");

            DisconnectClient(client);
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

                        client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                        break;
                }
            }

            catch (Exception ex)
            {
                logger.Error(() => $"OnClientRpcRequest: {request.Method}", ex);

                client.RespondError(StratumError.Other, ex.Message, request.Id);
            }
        }

        private async void OnClientSubscribe(StratumClient client, JsonRpcRequest request)
        {
            // var requestParams = request.Params?.ToObject<string[]>();
            // var userAgent = requestParams?.Length > 0 ? requestParams[0] : null;
            var response = await manager.HandleWorkerSubscribeAsync(client);
            
            var data = new object[]
            {
                new object[]
                {
                    new object[]
                    {
                        StratumConstants.MsgMiningNotify, response.SubscriptionId
                    },
                },
                response.Extranonce1,
                response.Extranonce2Length
            };

            client.Respond(data, request.Id);

            // Send difficulty
            // TODO

            // Send current job if available
            lock (currentJobParamsLock)
            {
                if (currentJobParams != null)
                {
                    client.Notify(StratumConstants.MsgMiningNotify, currentJobParams);
                }
            }
        }

        private async void OnClientAuthorize(StratumClient client, JsonRpcRequest request)
        {
            var requestParams = request.Params?.ToObject<string[]>();
            var workername = requestParams?.Length > 0 ? requestParams[0] : null;
            var password = requestParams?.Length > 1 ? requestParams[1] : null;

            client.IsAuthorized = await manager.HandleWorkerAuthenticateAsync(client, workername, password);
            client.Respond(client.IsAuthorized, request.Id);
        }

        private async void OnClientSubmitShare(StratumClient client, JsonRpcRequest request)
        {
            client.LastActivity = DateTime.UtcNow;

            if (!client.IsAuthorized)
                client.RespondError(StratumError.UnauthorizedWorker, "Unauthorized worker", request.Id);
            else if (!client.IsSubscribed)
                client.RespondError(StratumError.NotSubscribed, "Not subscribed", request.Id);
            else
            {
                var requestParams = request.Params?.ToObject<string[]>();
                var accepted = await manager.HandleWorkerSubmitAsync(client, requestParams);
                client.Respond(accepted, request.Id);
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

                        DisconnectClient(client);
                    }
                });
        }

        private void OnNewJob(object jobParams)
        {
            logger.Info(() => $"Received new job params from manager");

            lock (currentJobParamsLock)
            {
                currentJobParams = jobParams;
            }

            if(jobParams != null)
                BroadcastJob(jobParams);
        }

        private void BroadcastJob(object jobParams)
        {
            BroadcastNotification(StratumConstants.MsgMiningNotify, jobParams);
        }
    }
}
