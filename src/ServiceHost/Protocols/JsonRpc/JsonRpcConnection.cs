using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using MiningCore.Transport;
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
        public JsonRpcConnection(IConnection source)
        {
            this.source = source;

            // convert input into sequence of chars
            var incomingChars = source.Input
                .ObserveOn(TaskPoolScheduler.Default)
                .SelectMany(bytes => Encoding.UTF8.GetChars(bytes))
                .Publish()
                .RefCount();

            var incomingLines = incomingChars
                .Buffer(incomingChars.Where(c => c == '\n' || c == '\r')) // scan for newline
                .Select(c => new string(c.ToArray()).TrimEnd()); // transform buffer back to string

            var incomingNonEmptyLines = incomingLines
                .Where(x => x.Length > 0);

            Input = incomingNonEmptyLines
                .Select(x => 
                    JsonConvert.DeserializeObject<JsonRpcRequest>(x, serializerSettings))
                .Publish()
                .RefCount();

//            Input.Subscribe(x => {}, x => { });
        }

        private readonly IConnection source;

        private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

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
