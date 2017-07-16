using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using LibUvManaged;
using Microsoft.Extensions.Logging;
using MiningCore.Configuration;
using MiningCore.Configuration.Extensions;
using MiningCore.JsonRpc;

namespace MiningCore.Stratum
{
    public class StratumServer
    {
        public StratumServer(IComponentContext ctx, ILogger<StratumServer> logger)
        {
            this.ctx = ctx;
            this.logger = logger;

            ClientConnected = clientConnectedSubject.AsObservable();
            ClientDisconnected= clientDisconnectedSubject.AsObservable();
        }

        private readonly IComponentContext ctx;
        private readonly ILogger<StratumServer> logger;
        private readonly Dictionary<int, LibUvListener> ports = new Dictionary<int, LibUvListener>();
        private readonly Dictionary<string, StratumClient> clients = new Dictionary<string, StratumClient>();
        private readonly Subject<StratumClient> clientConnectedSubject = new Subject<StratumClient>();
        private readonly Subject<string> clientDisconnectedSubject = new Subject<string>();

        #region API-Surface

        public void Init(PoolConfig poolConfig)
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

        public IObservable<StratumClient> ClientConnected { get; }
        public IObservable<string> ClientDisconnected { get; }

        public int ClientCount
        {
            get
            {
                lock (clients)
                {
                    return clients.Count;
                }
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

            clientDisconnectedSubject.OnNext(subscriptionId);
        }

        public void SendBroadcast<T>(T payload, string id, Func<StratumClient, bool> filter = null)
        {
            SendBroadcast(new JsonRpcResponse<T>(payload, id), filter);
        }

        public void SendBroadcast<T>(JsonRpcResponse<T> response, Func<StratumClient, bool> filter = null)
        {
            Contract.RequiresNonNull(response, nameof(response));

            StratumClient[] tmp;

            lock (clients)
            {
                tmp = clients.Values.ToArray();
            }

            foreach (var client in tmp)
            {
                if (filter != null && !filter(client))
                    continue;

                client.Send(response);
            }
        }

        #endregion // API-Surface

        private void OnClientConnected(ILibUvConnection con, PoolEndpoint endpointConfig)
        {
            var subscriptionId = con.ConnectionId;

            var client = ctx.Resolve<StratumClient>(
                new TypedParameter(typeof(PoolEndpoint), endpointConfig));

            lock (clients)
            {
                clients[subscriptionId] = client;
            }

            client.Init(con, ctx);

            clientConnectedSubject.OnNext(client);
        }
    }
}
