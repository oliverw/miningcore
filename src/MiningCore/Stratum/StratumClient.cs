using System;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Autofac;
using CodeContracts;
using LibUvManaged;
using MiningCore.Configuration;
using MiningCore.JsonRpc;

namespace MiningCore.Stratum
{
    public class StratumClient
    {
        public StratumClient(PoolEndpoint endpointConfig)
        {
            this.config = endpointConfig;
        }

        private JsonRpcConnection rpcCon;
        private readonly PoolEndpoint config;
        private readonly StratumClientStats stats = new StratumClientStats();

        #region API-Surface

        public void Init(ILibUvConnection uvCon, IComponentContext ctx)
        {
            Contract.RequiresNonNull(uvCon, nameof(uvCon));

            rpcCon = ctx.Resolve<JsonRpcConnection>();
            rpcCon.Init(uvCon);

            Requests = rpcCon.Received;
        }

        public IObservable<JsonRpcRequest> Requests { get; private set; }
        public string SubscriptionId => rpcCon.ConnectionId;
        public IPEndPoint RemoteAddress => rpcCon.RemoteEndPoint;
        public bool IsAuthorized { get; set; } = false;
        public PoolEndpoint Config => config;
        public DateTime? LastActivity { get; set; }
        public object MiningContext { get; set; }
        public StratumClientStats Stats => stats;

        public T GetMiningContextAs<T>()
        {
            return (T) MiningContext;
        }

        public void Send<T>(T payload, string id)
        {
            Send(new JsonRpcResponse<T>(payload, id));
        }

        public void Send<T>(JsonRpcResponse<T> response)
        {
            lock (rpcCon)
            {
                rpcCon?.Send(response);
            }
        }

        public void SendError(StratumError code, string message, string id)
        {
            Send(new JsonRpcResponse(new JsonRpcException((int) code, message, null), id));
        }

        public void Disconnect()
        {
            lock (rpcCon)
            {
                rpcCon.Close();
            }
        }

        public void RespondError(string id, int code, string message)
        {
            Send(new JsonRpcResponse(new JsonRpcException(code, message, null), id));
        }

        public void RespondUnsupportedMethod(string id)
        {
            RespondError(id, 20, "Unsupported method");
        }

        public void RespondUnauthorized(string id)
        {
            RespondError(id, 24, "Unauthorized worker");
        }

        #endregion // API-Surface
    }
}
