using System;
using System.Collections.Generic;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using LibUvManaged;
using Microsoft.Extensions.Logging;
using MiningCore.Configuration;
using MiningCore.Configuration.Extensions;
using MiningCore.JsonRpc;
using Newtonsoft.Json;

namespace MiningCore.Stratum
{
    public class StratumServer
    {
        public StratumServer(IComponentContext ctx, ILogger<StratumServer> logger)
        {
            this.ctx = ctx;
            this.logger = logger;
        }

        private readonly IComponentContext ctx;
        private readonly ILogger<StratumServer> logger;
        private readonly Dictionary<int, LibUvListener> ports = new Dictionary<int, LibUvListener>();
        private readonly Dictionary<string, StratumClient> clients = new Dictionary<string, StratumClient>();

        public void Init(PoolConfig poolConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

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
                    listener.Start(endPoint, con => HandleNewClient(con, poolConfig));
                }, TaskCreationOptions.LongRunning);

                task.Start();

                logger.Info(() => $"Pool {poolConfig.Coin.Name}: Stratum port {port} started");
            }
        }

        private void HandleNewClient(ILibUvConnection con, PoolConfig poolConfig)
        {
            var subscriptionId = con.ConnectionId;

            var client = ctx.Resolve<StratumClient>(
                new TypedParameter(typeof(string), subscriptionId),
                new TypedParameter(typeof(PoolConfig), poolConfig));

            lock (clients)
            {
                clients[subscriptionId] = client;
            }

            client.Init(con, ctx);

            client.Requests
                .ObserveOn(TaskPoolScheduler.Default)
                .Subscribe(x=> OnClientRpcRequest(client, x), ex=> OnClientReceiveError(client, ex), ()=> OnClientReceiveComplete(client));
        }

        private void OnClientReceiveError(StratumClient client, Exception ex)
        {
            logger.Error(() => $"[{client.SubscriptionId}] Client connection entered error state: {ex.Message}");

            lock (clients)
            {
                clients.Remove(client.SubscriptionId);
            }

            client.Disconnect();
        }

        private void OnClientReceiveComplete(StratumClient client)
        {
            logger.Debug(() => $"[{client.SubscriptionId}] Received End-of-Stream from client");

            DisconnectClient(client);
        }

        private void OnClientRpcRequest(StratumClient client, JsonRpcRequest request)
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
                case StratumConstants.MsgGetTx:
                    OnClientGetTransaction(client, request);
                    break;

                default:
                    OnClientUnsupportedRequest(client, request);
                    break;
            }
        }

        private void OnClientSubscribe(StratumClient client, JsonRpcRequest request)
        {
            logger.Debug(() => $"[{client.SubscriptionId}] Subscribe received: {JsonConvert.SerializeObject(request.Params)}");

            client.Respond(new JsonRpcResponse(new { code = "Thanks" }, request.Id));
        }

        private void OnClientAuthorize(StratumClient client, JsonRpcRequest request)
        {
            logger.Debug(() => $"[{client.SubscriptionId}] Authorize received: {JsonConvert.SerializeObject(request.Params)}");
        }

        private void OnClientSubmitShare(StratumClient client, JsonRpcRequest request)
        {
            logger.Debug(() => $"[{client.SubscriptionId}] Submit received: {JsonConvert.SerializeObject(request.Params)}");

            lock (client)
            {
                client.LastActivity = DateTime.UtcNow;
            }
        }

        private void OnClientGetTransaction(StratumClient client, JsonRpcRequest request)
        {
            logger.Debug(() => $"[{client.SubscriptionId}] GetTransaction received: {JsonConvert.SerializeObject(request.Params)}");

            client.RespondErrorNotSupported(request.Id);
        }

        private void OnClientUnsupportedRequest(StratumClient client, JsonRpcRequest request)
        {
            logger.Warning(() => $"[{client.SubscriptionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request)}");
        }

        private void DisconnectClient(StratumClient client)
        {
            string subscriptionId = null;

            lock (client)
            {
                subscriptionId = client.SubscriptionId;
                client.Disconnect();
            }

            if (!string.IsNullOrEmpty(subscriptionId))
            {
                lock (clients)
                {
                    clients.Remove(client.SubscriptionId);
                }
            }
        }
    }
}
