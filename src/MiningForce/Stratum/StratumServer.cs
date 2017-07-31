using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using LibUvManaged;
using NLog;
using MiningForce.Configuration;
using MiningForce.JsonRpc;

namespace MiningForce.Stratum
{
    public abstract class StratumServer
    {
        protected StratumServer(IComponentContext ctx)
        {
	        Contract.RequiresNonNull(ctx, nameof(ctx));

			this.ctx = ctx;
        }

        protected readonly IComponentContext ctx;
        protected ILogger logger;
        protected readonly Dictionary<int, LibUvListener> ports = new Dictionary<int, LibUvListener>();

		protected readonly Dictionary<string, Tuple<StratumClient, IDisposable>> clients = 
			new Dictionary<string, Tuple<StratumClient, IDisposable>>();

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

                logger.Info(() => $"[{LogCat}] Stratum port {port} online");
            }
        }

        private void OnClientConnected(ILibUvConnection con, PoolEndpoint endpointConfig)
        {
            try
            {
                var subscriptionId = con.ConnectionId;

                var client = ctx.Resolve<StratumClient>();
                client.Init(con, ctx, endpointConfig);

	            lock (clients)
	            {
		            var sub = client.Requests
			            .ObserveOn(TaskPoolScheduler.Default)
			            .Subscribe(x => OnRequest(client, x), ex => OnReceiveError(client, ex), () => OnReceiveComplete(client));

					clients[subscriptionId] = Tuple.Create(client, sub);
	            }

				OnConnect(client);
			}

			catch (Exception ex)
            {
                logger.Error(ex, () => nameof(OnClientConnected));
            }
        }

        private void OnReceiveError(StratumClient client, Exception ex)
        {
            logger.Error(() => $"[{LogCat}] [{client.ConnectionId}] Connection error state: {ex.Message}");

            DisconnectClient(client);
        }

        private void OnReceiveComplete(StratumClient client)
        {
            logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Received EOF");

            DisconnectClient(client);
        }

        protected void DisconnectClient(StratumClient client)
        {
            Contract.RequiresNonNull(client, nameof(client));

            var subscriptionId = client.ConnectionId;

            if (!string.IsNullOrEmpty(subscriptionId))
            {
                lock (clients)
                {
	                Tuple<StratumClient, IDisposable> item;
					if (clients.TryGetValue(subscriptionId, out item))
	                {
		                item.Item2.Dispose();
		                clients.Remove(subscriptionId);
	                }
                }
            }

	        client.Disconnect();

			OnDisconnect(subscriptionId);
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
                tmp = clients.Values.Select(x=> x.Item1).ToArray();
            }

            foreach (var client in tmp)
            {
                if (filter != null && !filter(client))
                    continue;

                client.Notify(notification);
            }
        }

		protected abstract string LogCat { get; }

        protected abstract void OnConnect(StratumClient client);
        protected abstract void OnDisconnect(string subscriptionId);
	    protected abstract void OnRequest(StratumClient client, Timestamped<JsonRpcRequest> request);
    }
}
