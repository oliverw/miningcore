using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using LibUvManaged;
using Microsoft.Extensions.Logging;
using MiningCore.Configuration;
using MiningCore.Configuration.Extensions;
using MiningCore.JsonRpc;
using MiningCore.Stratum;

namespace MiningCore
{
    public class Pool
    {
        public Pool(IComponentContext ctx)
        {
            this.ctx = ctx;
            this.logger = ctx.Resolve<ILogger<Pool>>();
        }

        private readonly IComponentContext ctx;
        private readonly ILogger<Pool> logger;
        private StratumServer server;

        public Task InitAsync(Configuration.Pool poolConfig, ClusterConfiguration clusterConfig)
        {
            logger.Info(() => $"Pool {poolConfig.Coin.Name} initializing ...");

            InitializeStratum(poolConfig);

            return Task.FromResult(false);
        }

        private void InitializeStratum(Configuration.Pool poolConfig)
        {
            server = new StratumServer(ctx);
            server.Init(poolConfig);
        }
    }
}
