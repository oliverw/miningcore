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
using MiningForce.Configuration;
using MiningForce.Configuration.Extensions;
using MiningForce.JsonRpc;
using Newtonsoft.Json;

namespace MiningForce.Stratum
{
    public abstract class StratumServer
    {
        protected StratumServer(IComponentContext ctx, ILogger<StratumServer> logger, 
            JsonSerializerSettings serializerSettings)
        {
            this.ctx = ctx;
            this.logger = logger;
            this.serializerSettings = serializerSettings;
        }

        protected readonly IComponentContext ctx;
        protected readonly ILogger<StratumServer> logger;
        protected readonly Dictionary<int, LibUvListener> ports = new Dictionary<int, LibUvListener>();
        protected readonly Dictionary<string, StratumClient> clients = new Dictionary<string, StratumClient>();
        protected PoolConfig poolConfig;
        private readonly JsonSerializerSettings serializerSettings;

        protected void StartListeners(PoolConfig poolConfig)
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
                ports[port] = listener;

                // host it and its message loop in a dedicated background thread
                var task = new Task(() =>
                {
                    listener.Start(endPoint, con => OnClientConnected(con, endpointConfig));
                }, TaskCreationOptions.LongRunning);

                task.Start();

                logger.Info(() => $"[{poolConfig.Coin.Name}] Stratum port {port} started");
            }
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
            }

            catch (Exception ex)
            {
                logger.Error(() => "OnClientConnected", ex);
            }
        }

        private void OnClientRpcRequest(StratumClient client, JsonRpcRequest request)
        {
            logger.Debug(() => $"[{poolConfig.Coin.Name}] [{client.ConnectionId}] Received request {request.Method} [{request.Id}]: {JsonConvert.SerializeObject(request.Params, serializerSettings)}");

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
                        logger.Warning(() => $"[{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                        client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                        break;
                }
            }

            catch (Exception ex)
            {
                logger.Error(() => $"[{poolConfig.Coin.Name}] OnClientRpcRequest: {request.Method}", ex);

                client.RespondError(StratumError.Other, ex.Message, request.Id);
            }
        }

        private void OnClientReceiveError(StratumClient client, Exception ex)
        {
            logger.Error(() => $"[{poolConfig.Coin.Name}] [{client.ConnectionId}] Client connection entered error state: {ex.Message}");

            DisconnectClient(client);
        }

        private void OnClientReceiveComplete(StratumClient client)
        {
            logger.Debug(() => $"[{poolConfig.Coin.Name}] [{client.ConnectionId}] Received End-of-Stream from client");

            DisconnectClient(client);
        }

        protected void DisconnectClient(StratumClient client)
        {
            Contract.RequiresNonNull(client, nameof(client));

            var subscriptionId = client.ConnectionId;
            client.Disconnect();

            if (!string.IsNullOrEmpty(subscriptionId))
            {
                lock (clients)
                {
                    clients.Remove(subscriptionId);
                }
            }

            OnClientDisconnected(subscriptionId);
        }

        protected void BroadcastNotification<T>(string method, T payload, Func<StratumClient, bool> filter = null)
        {
            BroadcastNotification(new JsonRpcRequest<T>(method, payload, null), filter);
        }

        protected void BroadcastNotification<T>(JsonRpcRequest<T> notification, Func<StratumClient, bool> filter = null)
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

        protected abstract void OnClientConnected(StratumClient client);
        protected abstract void OnClientDisconnected(string subscriptionId);

        protected abstract void OnClientSubscribe(StratumClient client, JsonRpcRequest request);
        protected abstract void OnClientAuthorize(StratumClient client, JsonRpcRequest request);
        protected abstract void OnClientSubmitShare(StratumClient client, JsonRpcRequest request);
    }
}
