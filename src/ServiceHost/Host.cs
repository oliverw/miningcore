using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using MiningCore.Configuration;
using MiningCore.Configuration.Extensions;
using MiningCore.Protocols.JsonRpc;
using MiningCore.Transport;

namespace MiningCore
{
    public class Host
    {
        public Host(
            IComponentContext ctx,
            ILogger<Host> logger)
        {
            this.ctx = ctx;
            this.logger = logger;
        }

        private readonly ILogger<Host> logger;
        private readonly IComponentContext ctx;
        private Dictionary<string, IEndpointDispatcher> endPointDispatchers = new Dictionary<string, IEndpointDispatcher>();

        public void Start(PoolConfiguration config)
        {
            StartEndpoints(config);
        }

        private void StartEndpoints(PoolConfiguration config)
        {
            foreach (var endpointConfig in config.Endpoints)
            {
                logger.Info(() => $"Starting endpoint {endpointConfig.Id} @ {endpointConfig.Address}:{endpointConfig.Port}");

                var dispatcher = ctx.Resolve<IEndpointDispatcher>();
                var endPoint = new IPEndPoint(IPAddress.Parse(endpointConfig.Address), endpointConfig.Port);
                endPointDispatchers[endpointConfig.Id] = dispatcher;

                dispatcher.EndpointId = endpointConfig.Id;

                var task = new Task(()=> dispatcher.Start(endPoint, (con) => new JsonRpcConnection(ctx, con)), TaskCreationOptions.LongRunning);
                task.Start();
            }
        }
    }
}
