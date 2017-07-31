using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using CodeContracts;
using LibUvManaged;
using NLog;
using Newtonsoft.Json;

// http://www.jsonrpc.org/specification
// https://github.com/Astn/JSON-RPC.NET
//
// A Notification is a Request object without an "id" member.

namespace MiningForce.JsonRpc
{
    public class JsonRpcConnection
    {
        public JsonRpcConnection(JsonSerializerSettings serializerSettings)
        {
	        Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));

			this.serializerSettings = serializerSettings;
        }

        private readonly JsonSerializerSettings serializerSettings;
        private readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private ILibUvConnection upstream;
        private const int MaxRequestLength = 8192;

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

            // buffer until newline detected
            var incomingLines = incomingChars
                .Buffer(incomingChars.Where(c => c == '\n' || c == '\r')) // scan for newline
                .TakeWhile(ValidateInput)  // flood protetion
                .Select(c => new string(c.ToArray()).Trim()); // transform back to string

            Received = incomingLines
		        .Where(line => line.Length > 0) // ignore empty lines
				.Select(line => new { Json = line, Request = JsonConvert.DeserializeObject<JsonRpcRequest>(line, serializerSettings) })
                .Do(x => logger.Debug(() => $"[{ConnectionId}] Received JsonRpc-Request: {x.Json}"))
                .Select(x => x.Request)
				.Timestamp()
                .Publish()
                .RefCount();
        }

        public IObservable<Timestamped<JsonRpcRequest>> Received { get; private set; }

        public void Send<T>(JsonRpcResponse<T> response)
        {
            var json = JsonConvert.SerializeObject(response, serializerSettings) + "\n";
	        logger.Debug(() => $"[{ConnectionId}] Sending response: {json.Trim()}");

			upstream.Send(Encoding.UTF8.GetBytes(json));
        }

        public void Send<T>(JsonRpcRequest<T> request)
        {
            var json = JsonConvert.SerializeObject(request, serializerSettings) + "\n";
	        logger.Debug(() => $"[{ConnectionId}] Sending request: {json.Trim()}");

			upstream.Send(Encoding.UTF8.GetBytes(json));
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
            var isInvalid = chars.Count > MaxRequestLength;

            if (isInvalid)
                logger.Error(() => $"[{upstream.ConnectionId}] Incoming message too big. Closing");

            return !isInvalid;
        }
    }
}
