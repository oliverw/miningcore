using System;
using System.Linq;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using Autofac;
using LibUvManaged;
using Microsoft.Extensions.Logging;
using MiningCore.Configuration.Extensions;
using MiningCore.Protocols.JsonRpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

// http://www.jsonrpc.org/specification
// https://github.com/Astn/JSON-RPC.NET
//
// A Notification is a Request object without an "id" member.

namespace MiningCore.JsonRpc
{
    public class JsonRpcConnection
    {
        public JsonRpcConnection(IComponentContext ctx,
            ILibUvConnection upstream)
        {
            this.logger = ctx.Resolve<ILogger<JsonRpcConnection>>();
            this.upstream = upstream;

            // convert input into sequence of chars
            var incomingChars = upstream.Received
                .ObserveOn(TaskPoolScheduler.Default)
                .SelectMany(bytes => Encoding.UTF8.GetChars(bytes))
                .Publish()
                .RefCount();

            var incomingLines = incomingChars
                .Buffer(incomingChars.Where(c => c == '\n' || c == '\r')) // scan for newline
                .Select(c => new string(c.ToArray()).Trim()); // transform buffer back to string

            var incomingNonEmptyLines = incomingLines
                .Where(x => x.Length > 0);

            Received = incomingNonEmptyLines
                .Select(x => new { Json = x, Msg = JsonConvert.DeserializeObject<JsonRpcRequest>(x, serializerSettings) })
                .Do(x=> logger.Debug(()=> $"[{ConnectionId}] Received JsonRpc-Request: {x.Json}"))
                .Select(x=> x.Msg)
                .Replay(1)
                .Publish()
                .RefCount();

            Received.Subscribe(x => {}, x => { });
        }

        private readonly ILibUvConnection upstream;

        private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private readonly ILogger<JsonRpcConnection> logger;

        #region Implementation of IJsonRpcConnection

        public IObservable<JsonRpcRequest> Received { get; }
        public IObserver<JsonRpcResponse> Output { get; }

        public void Send(JsonRpcResponse response)
        {
            var json = JsonConvert.SerializeObject(response);
            var bytes = Encoding.UTF8.GetBytes(json);

            upstream.Send(bytes);
        }

        public IPEndPoint RemoteEndPoint => upstream?.RemoteEndPoint;
        public string ConnectionId => upstream?.ConnectionId;

        public void Close()
        {
            upstream.Close();
        }

        #endregion
    }
}
