using System;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Autofac;
using CodeContracts;
using LibUvManaged;
using MiningForce.Configuration;
using MiningForce.JsonRpc;

namespace MiningForce.Stratum
{
    public class StratumClient<TContext>
	{
        private JsonRpcConnection rpcCon;
        private PoolEndpoint config;

		#region API-Surface

		public void Init(ILibUvConnection uvCon, IComponentContext ctx, PoolEndpoint endpointConfig)
        {
	        Contract.RequiresNonNull(uvCon, nameof(uvCon));
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(endpointConfig, nameof(endpointConfig));

	        config = endpointConfig;

			rpcCon = ctx.Resolve<JsonRpcConnection>();
            rpcCon.Init(uvCon);

            Requests = rpcCon.Received;
		}

		public TContext Context { get; set; }
		public IObservable<Timestamped<JsonRpcRequest>> Requests { get; private set; }
		public string ConnectionId => rpcCon.ConnectionId;
        public PoolEndpoint PoolEndpoint => config;
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
	        Contract.RequiresNonNull(id, nameof(id));

			Respond(new JsonRpcResponse(new JsonRpcException((int)code, message, null), id, result));
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
	        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(message), $"{nameof(message)} must not be empty");

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
