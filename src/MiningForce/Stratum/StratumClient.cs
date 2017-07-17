using System;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Autofac;
using CodeContracts;
using LibUvManaged;
using MiningForce.Configuration;
using MiningForce.JsonRpc;
using MiningForce.VarDiff;

namespace MiningForce.Stratum
{
    public class StratumClient
    {
        public StratumClient(PoolEndpoint endpointConfig)
        {
            this.config = endpointConfig;
        }

        private JsonRpcConnection rpcCon;
        private readonly PoolEndpoint config;

        #region API-Surface

        public void Init(ILibUvConnection uvCon, IComponentContext ctx)
        {
            Contract.RequiresNonNull(uvCon, nameof(uvCon));

            rpcCon = ctx.Resolve<JsonRpcConnection>();
            rpcCon.Init(uvCon);

            Requests = rpcCon.Received;
        }

        public IObservable<JsonRpcRequest> Requests { get; private set; }
        public string ConnectionId => rpcCon.ConnectionId;
        public PoolEndpoint PoolEndpoint => config;
        public IPEndPoint RemoteEndpoint => rpcCon.RemoteEndPoint;

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
