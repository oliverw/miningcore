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
using NLog;
using MiningForce.Configuration;
using MiningForce.JsonRpc;
using Newtonsoft.Json;

namespace MiningForce.Stratum
{
    public abstract class StratumServer
    {
        protected StratumServer(IComponentContext ctx, ILogger logger, 
            JsonSerializerSettings serializerSettings)
        {
            this.ctx = ctx;
            this.logger = logger;
            this.serializerSettings = serializerSettings;
        }

        protected readonly IComponentContext ctx;
        protected readonly ILogger logger;
        protected readonly Dictionary<int, LibUvListener> ports = new Dictionary<int, LibUvListener>();
        protected readonly Dictionary<string, StratumClient> clients = new Dictionary<string, StratumClient>();
        private readonly JsonSerializerSettings serializerSettings;

        protected void StartListeners(Dictionary<int, PoolEndpoint> stratumPorts)
        {
            Contract.RequiresNonNull(ports, nameof(ports));

            // start ports
            foreach (var port in stratumPorts.Keys)
            {
                var endpointConfig = stratumPorts[port];

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
                var listener = new LibUvListener();
                ports[port] = listener;

                // host it and its message loop in a dedicated background thread
                var task = new Task(() =>
                {
                    listener.Start(endPoint, con => OnClientConnected(con, endpointConfig));
                }, TaskCreationOptions.LongRunning);

                task.Start();

                logger.Info(() => $"[{LoggingPrefix}] Stratum port {port} online");
            }
        }

        private void OnClientConnected(ILibUvConnection con, PoolEndpoint endpointConfig)
        {
            try
            {
                var subscriptionId = con.ConnectionId;

                var client = ctx.Resolve<StratumClient>();

                lock (clients)
                {
                    clients[subscriptionId] = client;
                }

                client.Init(con, ctx, endpointConfig);

				// monitor client requests
				client.Requests
                    .ObserveOn(TaskPoolScheduler.Default)
                    .Subscribe(x => OnClientRpcRequest(client, x), ex => OnClientReceiveError(client, ex), () => OnClientReceiveComplete(client));

	            OnClientConnected(client);
			}

			catch (Exception ex)
            {
                logger.Error(ex, () => "OnClientConnected");
            }
        }

        private void OnClientRpcRequest(StratumClient client, JsonRpcRequest request)
        {
            logger.Debug(() => $"[{LoggingPrefix}] [{client.ConnectionId}] Received request {request.Method} [{request.Id}]: {JsonConvert.SerializeObject(request.Params, serializerSettings)}");

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
                        logger.Warn(() => $"[{LoggingPrefix}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                        client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                        break;
                }
            }

            catch (Exception ex)
            {
                logger.Error(ex, () => $"OnClientRpcRequest: {request.Method}");

                client.RespondError(StratumError.Other, ex.Message, request.Id);
            }
        }

        private void OnClientReceiveError(StratumClient client, Exception ex)
        {
            logger.Error(() => $"[{LoggingPrefix}] [{client.ConnectionId}] Connection error state: {ex.Message}");

            DisconnectClient(client);
        }

        private void OnClientReceiveComplete(StratumClient client)
        {
            logger.Debug(() => $"[{LoggingPrefix}] [{client.ConnectionId}] Received EOF");

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

		protected abstract string LoggingPrefix { get; }

        protected abstract void OnClientConnected(StratumClient client);
        protected abstract void OnClientDisconnected(string subscriptionId);

        protected abstract void OnClientSubscribe(StratumClient client, JsonRpcRequest request);
        protected abstract void OnClientAuthorize(StratumClient client, JsonRpcRequest request);
        protected abstract void OnClientSubmitShare(StratumClient client, JsonRpcRequest request);
    }
}
