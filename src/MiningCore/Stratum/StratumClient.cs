using System;
using System.Net;
using System.Reactive;
using Autofac;
using MiningCore.JsonRpc;
using NetUV.Core.Handles;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Stratum
{
    public class StratumClient<TContext>
    {
        private JsonRpcConnection rpcCon;

        #region API-Surface

        public void Init(Tcp uvCon, IComponentContext ctx, IPEndPoint endpointConfig, string connectionId)
        {
            Contract.RequiresNonNull(uvCon, nameof(uvCon));
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(endpointConfig, nameof(endpointConfig));

            PoolEndpoint = endpointConfig;

            rpcCon = ctx.Resolve<JsonRpcConnection>();
            rpcCon.Init(uvCon, connectionId);

            Requests = rpcCon.Received;
        }

        public TContext Context { get; set; }
        public IObservable<Timestamped<JsonRpcRequest>> Requests { get; private set; }
        public string ConnectionId => rpcCon.ConnectionId;
        public IPEndPoint PoolEndpoint { get; private set; }

        public IPEndPoint RemoteEndpoint => rpcCon.RemoteEndPoint;

        public void Respond<T>(T payload, object id)
        {
            Contract.RequiresNonNull(payload, nameof(payload));
            Contract.RequiresNonNull(id, nameof(id));

            Respond(new JsonRpcResponse<T>(payload, id));
        }

        public void RespondError(StratumError code, string message, object id, object result = null, object data = null)
        {
            Contract.RequiresNonNull(message, nameof(message));

            Respond(new JsonRpcResponse(new JsonRpcException((int) code, message, null), id, result));
        }

        public void Respond<T>(JsonRpcResponse<T> response)
        {
            Contract.RequiresNonNull(response, nameof(response));

            lock (rpcCon)
            {
                rpcCon?.Send(response);
            }
        }

        public void Notify<T>(string method, T payload)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            Notify(new JsonRpcRequest<T>(method, payload, null));
        }

        public void Notify<T>(JsonRpcRequest<T> request)
        {
            Contract.RequiresNonNull(request, nameof(request));

            lock (rpcCon)
            {
                rpcCon?.Send(request);
            }
        }

        public void Disconnect()
        {
            lock (rpcCon)
            {
                rpcCon?.Close();
            }
        }

        public void RespondError(object id, int code, string message)
        {
            Contract.RequiresNonNull(id, nameof(id));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(message),
                $"{nameof(message)} must not be empty");

            Respond(new JsonRpcResponse(new JsonRpcException(code, message, null), id));
        }

        public void RespondUnsupportedMethod(object id)
        {
            Contract.RequiresNonNull(id, nameof(id));

            RespondError(id, 20, "Unsupported method");
        }

        public void RespondUnauthorized(object id)
        {
            Contract.RequiresNonNull(id, nameof(id));

            RespondError(id, 24, "Unauthorized worker");
        }

        #endregion // API-Surface
    }
}
