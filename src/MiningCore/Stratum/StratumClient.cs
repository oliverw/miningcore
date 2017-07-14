using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Text;
using Autofac;
using Microsoft.Extensions.Logging;
using MiningCore.Configuration;
using MiningCore.Configuration.Extensions;
using MiningCore.JsonRpc;
using MiningCore.Protocols.JsonRpc;

namespace MiningCore.Stratum
{
    class StratumClient
    {
        public StratumClient(IComponentContext ctx, string subscriptionId,
            NetworkEndpoint endpointConfig, Configuration.Pool poolConfig )
        {
            this.logger = ctx.Resolve<ILogger<StratumClient>>();
            this.ctx = ctx;
            this.subscriptionId = subscriptionId;
            this.endpointConfig = endpointConfig;
            this.poolConfig = poolConfig;
        }

        private IComponentContext ctx;
        private readonly string subscriptionId;
        private NetworkEndpoint endpointConfig;
        private Configuration.Pool poolConfig;
        private JsonRpcConnection con;
        private readonly CompositeDisposable cleanup = new CompositeDisposable();
        private readonly ILogger<StratumClient> logger;

        public void Init(JsonRpcConnection con)
        {
            this.con = con;

            cleanup.Add(this.con.Received.Subscribe(OnRpcMessage, OnReceiveError, OnReceiveComplete));
        }

        private void OnReceiveError(Exception ex)
        {
            logger.Debug(() => $"[{subscriptionId}]: Connection entered error state: {ex.Message}");

            con.Close();
        }

        private void OnReceiveComplete()
        {
            logger.Debug(() => $"[{subscriptionId}]: Received End-of-Stream");

            con.Close();
        }

        private void OnRpcMessage(JsonRpcRequest request)
        {
        }
    }
}
