using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using Autofac;
using CodeContracts;
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
        public JsonRpcConnection(IComponentContext ctx)
        {
            this.logger = ctx.Resolve<ILogger<JsonRpcConnection>>();
        }

        private readonly ILogger<JsonRpcConnection> logger;
        private ILibUvConnection upstream;
        private const int MaxRequestLength = 8192;

        private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        #region Implementation of IJsonRpcConnection

        public void Init(ILibUvConnection upstream)
        {
            Contract.RequiresNonNull(upstream, nameof(upstream));

            this.upstream = upstream;

            // convert input into sequence of chars
            var incomingChars = upstream.Received
                .SelectMany(bytes => Encoding.UTF8.GetChars(bytes))
                .Publish()
                .RefCount();

            var incomingLines = incomingChars
                .Buffer(incomingChars.Where(c => c == '\n' || c == '\r')) // scan for newline
                .TakeWhile(ValidateInput)  // flood protetion
                .Select(c => new string(c.ToArray()).Trim()); // transform buffer back to string

            var incomingNonEmptyLines = incomingLines
                .Where(x => x.Length > 0);

            Received = incomingNonEmptyLines
                .Select(x => new { Json = x, Msg = JsonConvert.DeserializeObject<JsonRpcRequest>(x, serializerSettings) })
                .Do(x => logger.Debug(() => $"[{ConnectionId}] Received JsonRpc-Request: {x.Json}"))
                .Select(x => x.Msg)
                .Publish()
                .RefCount();
        }

        public IObservable<JsonRpcRequest> Received { get; private set; }

        public void Send(JsonRpcResponse response)
        {
            Contract.RequiresNonNull(response, nameof(response));

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

        private bool ValidateInput(IList<char> chars)
        {
            var messageTooLong = chars.Count > MaxRequestLength;

            if (messageTooLong)
                logger.Error(() => $"[{upstream.ConnectionId}] Incoming message too big. Closing");

            return !messageTooLong;
        }
    }
}
