using System;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
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

		// telemetry
		private readonly Subject<string> responses = new Subject<string>();

        #region API-Surface

        public void Init(ILibUvConnection uvCon, IComponentContext ctx)
        {
            Contract.RequiresNonNull(uvCon, nameof(uvCon));

            rpcCon = ctx.Resolve<JsonRpcConnection>();
            rpcCon.Init(uvCon);

            Requests = rpcCon.Received;

			// Telemetry
			ResponseTime = Requests
		        .Where(x => !string.IsNullOrEmpty(x.Id))
		        .Select(req => new {Request = req, Start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()})
		        .SelectMany(req => responses
			        .Where(reqId => reqId == req.Request.Id)
			        .Select(_ => (int) (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - req.Start))
			        .Take(1))
		        .Publish()
		        .RefCount();
        }

		public IObservable<JsonRpcRequest> Requests { get; private set; }
		public string ConnectionId => rpcCon.ConnectionId;
        public PoolEndpoint PoolEndpoint => config;
        public IPEndPoint RemoteEndpoint => rpcCon.RemoteEndPoint;
	    public IObservable<int> ResponseTime { get; private set; }

		public void Respond<T>(T payload, string id)
        {
	        Contract.RequiresNonNull(payload, nameof(payload));
	        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(id), $"{nameof(id)} must not be empty");

			Respond(new JsonRpcResponse<T>(payload, id));
        }

        public void RespondError(StratumError code, string message, string id, object result = null, object data = null)
        {
	        Contract.RequiresNonNull(message, nameof(message));
	        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(id), $"{nameof(id)} must not be empty");

			Respond(new JsonRpcResponse(new JsonRpcException((int)code, message, null), id, result));
        }

        public void Respond<T>(JsonRpcResponse<T> response)
        {
	        Contract.RequiresNonNull(response, nameof(response));

			if (!string.IsNullOrEmpty(response.Id))
		        responses.OnNext(response.Id);

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
                rpcCon.Close();
            }
        }

        public void RespondError(string id, int code, string message)
        {
	        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(id), $"{nameof(id)} must not be empty");
	        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(message), $"{nameof(message)} must not be empty");

			Respond(new JsonRpcResponse(new JsonRpcException(code, message, null), id));
        }

        public void RespondUnsupportedMethod(string id)
        {
	        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(id), $"{nameof(id)} must not be empty");
	        
			RespondError(id, 20, "Unsupported method");
        }

        public void RespondUnauthorized(string id)
        {
	        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(id), $"{nameof(id)} must not be empty");

			RespondError(id, 24, "Unauthorized worker");
        }

        #endregion // API-Surface
    }
}
