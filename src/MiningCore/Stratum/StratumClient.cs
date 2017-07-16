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
        public bool IsSubscribed => WorkerContext != null;
        public PoolEndpoint Config => config;
        public DateTime? LastActivity { get; set; }
        public object WorkerContext { get; set; }
        public double Difficulty { get; set; }
        public double PreviousDifficulty { get; set; }
        public StratumClientStats Stats => stats;

        public T GetWorkerContextAs<T>()
        {
            return (T) WorkerContext;
        }

        public void Respond<T>(T payload, string id)
        {
            Respond(new JsonRpcResponse<T>(payload, id));
        }

        public void RespondError(StratumError code, string message, string id, object data = null)
        {
            Respond(new JsonRpcResponse(new JsonRpcException((int)code, message, null), id));
        }

        public void Respond<T>(JsonRpcResponse<T> response)
        {
            lock (rpcCon)
            {
                rpcCon?.Send(response);
            }
        }

        public void Notify<T>(string method, T payload)
        {
            Notify(new JsonRpcRequest<T>(method, payload, null));
        }

        public void Notify<T>(JsonRpcRequest<T> request)
        {
            lock (rpcCon)
            {
                rpcCon?.Send(request);
            }
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
            Respond(new JsonRpcResponse(new JsonRpcException(code, message, null), id));
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
