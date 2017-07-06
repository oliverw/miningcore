using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using Autofac;
using Microsoft.Extensions.Logging;
using MiningCore.Configuration.Extensions;
using MiningCore.Transport;
using MiningCore.Transport.LibUv;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

// http://www.jsonrpc.org/specification
// https://github.com/Astn/JSON-RPC.NET
//
// A Notification is a Request object without an "id" member.

namespace MiningCore.Protocols.JsonRpc
{
    public class JsonRpcConnection : IJsonRpcConnection
    {
        public JsonRpcConnection(IComponentContext ctx,
            IConnection source)
        {
            this.logger = ctx.Resolve<ILogger<JsonRpcConnection>>();
            this.source = source;

            // convert input into sequence of chars
            var incomingChars = source.Input
                .ObserveOn(TaskPoolScheduler.Default)
                .SelectMany(bytes => Encoding.UTF8.GetChars(bytes))
                .Publish()
                .RefCount();

            var incomingLines = incomingChars
                .Buffer(incomingChars.Where(c => c == '\n' || c == '\r')) // scan for newline
                .Select(c => new string(c.ToArray()).Trim()); // transform buffer back to string

            var incomingNonEmptyLines = incomingLines
                .Where(x => x.Length > 0);

            Input = incomingNonEmptyLines
                .Select(x => new { Json = x, Msg = JsonConvert.DeserializeObject<JsonRpcRequest>(x, serializerSettings) })
                .Do(x=> logger.Debug(()=> $"[{ConnectionId}] Received JsonRpc-Request: {x.Json}"))
                .Select(x=> x.Msg)
                .Publish()
                .RefCount();

            Input.Subscribe(x => {}, x => { });
        }

        private readonly IConnection source;

        private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private readonly ILogger<JsonRpcConnection> logger;

        #region Implementation of IJsonRpcConnection

        public IObservable<JsonRpcRequest> Input { get; }
        public IObserver<JsonRpcResponse> Output { get; }
        public IPEndPoint RemoteEndPoint => source?.RemoteEndPoint;
        public string ConnectionId => source?.ConnectionId;

        public void Close()
        {
            source.Close();
        }

        #endregion
    }
}
