using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Autofac;
using CodeContracts;
using LibUvManaged;
using MiningCore.JsonRpc;

namespace MiningCore.Stratum
{
    public class StratumClient
    {
        private JsonRpcConnection rpcCon;

        public void Init(ILibUvConnection uvCon, IComponentContext ctx)
        {
            Contract.RequiresNonNull(uvCon, nameof(uvCon));

            rpcCon = new JsonRpcConnection(ctx);
            rpcCon.Init(uvCon);

            Requests = rpcCon.Received;
        }

        public IObservable<JsonRpcRequest> Requests { get; private set; }
        public string SubscriptionId => rpcCon.ConnectionId;
        public DateTime LastActivity { get; set; }

        public void Respond<T>(T response) where T : JsonRpcResponse
        {
            rpcCon.Send(response);
        }

        public void Disconnect()
        {
            rpcCon?.Close();
            rpcCon = null;
        }

        public void RespondError(string id, int code, string message)
        {
            Respond(new JsonRpcResponse(new JsonRpcException(code, message, null), id));
        }

        public void RespondErrorNotSupported(string id)
        {
            Respond(new JsonRpcResponse(new JsonRpcException(20, "Unsupported method", null), id));
        }
    }
}
