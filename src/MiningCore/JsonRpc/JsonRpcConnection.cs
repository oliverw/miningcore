using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using CodeContracts;
using NetUV.Core.Handles;
using NLog;
using Newtonsoft.Json;

// http://www.jsonrpc.org/specification
// https://github.com/Astn/JSON-RPC.NET
//
// A Notification is a Request object without an "id" member.

namespace MiningCore.JsonRpc
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
        private Tcp upstream;
        private const int MaxRequestLength = 8192;

        #region Implementation of IJsonRpcConnection

        public void Init(Tcp upstream)
        {
            Contract.RequiresNonNull(upstream, nameof(upstream));

            this.upstream = upstream;

            // convert input into sequence of chars
            var incomingLines = Observable.Create<string>(observer =>
                {
                    upstream.OnRead((handle, buffer) =>
                    {
                        // onAccept
                        var data = buffer.ReadString(Encoding.UTF8, new[] {(byte) 10}).Trim();

                        if (data.Length < MaxRequestLength)
                            observer.OnNext(data);
                        else
                            observer.OnError(
                                new InvalidDataException(
                                    $"[{upstream.UserToken}] Incoming message exceeds maximum length of {MaxRequestLength}"));
                    }, (handle, ex) =>
                    {
                        // onError
                        observer.OnError(ex);
                    }, handle =>
                    {
                        // onCompleted
                        observer.OnCompleted();
                    });

                    return Disposable.Create(() =>
                    {
                        if (upstream.IsValid)
                            logger.Debug(
                                () => $"[{upstream.UserToken}] Last subscriber disconnected from receiver stream");

                        upstream.Dispose();
                    });
                })
                .Publish()
                .RefCount();

            Received = incomingLines
                .Where(line => line.Length > 0) // ignore empty lines
                .Select(line => new
                {
                    Json = line,
                    Request = JsonConvert.DeserializeObject<JsonRpcRequest>(line, serializerSettings)
                })
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

            upstream.QueueWrite(Encoding.UTF8.GetBytes(json));
        }

        public void Send<T>(JsonRpcRequest<T> request)
        {
            var json = JsonConvert.SerializeObject(request, serializerSettings) + "\n";
            logger.Debug(() => $"[{ConnectionId}] Sending request: {json.Trim()}");

            upstream.QueueWrite(Encoding.UTF8.GetBytes(json));
        }

        public IPEndPoint RemoteEndPoint => upstream?.GetPeerEndPoint();
        public string ConnectionId => (string) upstream?.UserToken;

        public void Close()
        {
            upstream.CloseHandle();
        }

        #endregion
    }
}