using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Autofac;
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
        }

        private readonly IComponentContext ctx;
        private readonly ILogger<StratumServer> logger;
        private readonly Dictionary<int, LibUvListener> ports = new Dictionary<int, LibUvListener>();
        private readonly Dictionary<string, StratumClient> clients = new Dictionary<string, StratumClient>();

        public void Init(PoolConfig poolConfig)
        {
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

            client.Init(con);
        }
    }
}
