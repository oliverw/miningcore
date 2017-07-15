using System;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Autofac;
using LibUvManaged;
using Microsoft.Extensions.Logging;
using MiningCore.Configuration;
using MiningCore.Configuration.Extensions;
using MiningCore.JsonRpc;
using MiningCore.Protocols.JsonRpc;
using Newtonsoft.Json;

namespace MiningCore.Stratum
{
    class StratumClient
    {
        public StratumClient(IComponentContext ctx, ILogger<StratumClient> logger,
            string subscriptionId, PoolConfig poolConfig )
        {
            this.logger = logger;
            this.ctx = ctx;
            this.subscriptionId = subscriptionId;
            this.poolConfig = poolConfig;
        }

        private readonly IComponentContext ctx;
        private JsonRpcConnection rpcCon;
        private readonly string subscriptionId;
        private PoolConfig poolConfig;
        private readonly CompositeDisposable cleanup = new CompositeDisposable();
        private readonly ILogger<StratumClient> logger;

        public void Init(ILibUvConnection uvCon)
        {
            rpcCon = new JsonRpcConnection(ctx);
            rpcCon.Init(uvCon);

            cleanup.Add(rpcCon.Received
                .ObserveOn(TaskPoolScheduler.Default)
                .Subscribe(OnRpcMessage, OnReceiveError, OnReceiveComplete));
        }

        private void OnReceiveError(Exception ex)
        {
            logger.Error(() => $"[{subscriptionId}]: Connection entered error state: {ex.Message}");

            rpcCon.Close();
        }

        private void OnReceiveComplete()
        {
            logger.Debug(() => $"[{subscriptionId}]: Received End-of-Stream");

            rpcCon.Close();
        }

        private void OnRpcMessage(JsonRpcRequest request)
        {
            logger.Info(()=> JsonConvert.SerializeObject(request));
        }
    }
}
