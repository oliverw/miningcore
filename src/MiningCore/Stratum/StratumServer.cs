using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
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
        public StratumServer(IComponentContext ctx)
        {
            this.ctx = ctx;
            this.logger = ctx.Resolve<ILogger<StratumServer>>();
        }

        private readonly IComponentContext ctx;
        private readonly ILogger<StratumServer> logger;

        private readonly ConcurrentDictionary<int, Tuple<Task, LibUvListener>> ports =
            new ConcurrentDictionary<int, Tuple<Task, LibUvListener>>();

        private readonly ConcurrentDictionary<string, StratumClient> clients =
            new ConcurrentDictionary<string, StratumClient>();

        public void Init(Configuration.Pool poolConfig)
        {
            foreach (var endpointConfig in poolConfig.Ports)
            {
                var listener = new LibUvListener(ctx);
                var endPoint = new IPEndPoint(IPAddress.Parse(endpointConfig.Address), endpointConfig.Port);

                var task = new Task(() => listener.Start(endPoint, con =>
                    HandleNewClient(con, endpointConfig, poolConfig)), TaskCreationOptions.LongRunning);

                ports[endpointConfig.Port] = Tuple.Create(task, listener);
                task.Start();

                logger.Info(() => $"Pool {poolConfig.Coin.Name}: Stratum port {endpointConfig.Port} started");
            }
        }

        private void HandleNewClient(ILibUvConnection con, NetworkEndpoint endpointConfig, Configuration.Pool poolConfig)
        {
            var subscriptionId = con.ConnectionId;
            var client = new StratumClient(ctx, subscriptionId, endpointConfig, poolConfig);
            clients[subscriptionId] = client;

            var rpcCon = new JsonRpcConnection(ctx, con);
            client.Init(rpcCon);
        }
    }
}
