using System;
using System.IO;
using System.Net;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using NetUV.Core.Handles;
using Newtonsoft.Json;
using NLog;
using Contract = MiningCore.Contracts.Contract;

// http://www.jsonrpc.org/specification
// https://github.com/Astn/JSON-RPC.NET

namespace MiningCore.JsonRpc
{
    public class JsonRpcConnection
    {
        public JsonRpcConnection(JsonSerializerSettings serializerSettings)
        {
            Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));

            this.serializerSettings = serializerSettings;
        }

        private readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private readonly JsonSerializerSettings serializerSettings;
        private Tcp upstream;
        private const int MaxRequestLength = 8192;

        #region Implementation of IJsonRpcConnection

        public void Init(Tcp upstream)
        {
            Contract.RequiresNonNull(upstream, nameof(upstream));

            this.upstream = upstream;

            var incomingLines = Observable.Create<string>(observer =>
                {
                    var sb = new StringBuilder();

                    upstream.OnRead((handle, buffer) =>
                    {
                        // onAccept
                        var data = buffer.ReadString(Encoding.UTF8);

                        if (!string.IsNullOrEmpty(data))
                        {
                            // flood-prevention check
                            if (sb.Length + data.Length < MaxRequestLength)
                            {
                                sb.Append(data);

                                // scan for lines and emit
                                int index;
                                while (sb.Length > 0 && (index = sb.ToString().IndexOf('\n')) != -1)
                                {
                                    var line = sb.ToString(0, index).Trim();
                                    sb.Remove(0, index + 1);

                                    if(line.Length > 0)
                                        observer.OnNext(line);
                                }
                            }

                            else
                            {
                                observer.OnError(new InvalidDataException($"[{upstream.UserToken}] Incoming message exceeds maximum length of {MaxRequestLength}"));
                            }
                        }
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
                            logger.Debug(() => $"[{upstream.UserToken}] Last subscriber disconnected from receiver stream");

                        upstream.Dispose();
                    });
                });

            Received = incomingLines
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

            try
            {
                upstream.QueueWrite(Encoding.UTF8.GetBytes(json));
            }
            catch (ObjectDisposedException)
            {
                // ignored
            }
        }

        public void Send<T>(JsonRpcRequest<T> request)
        {
            var json = JsonConvert.SerializeObject(request, serializerSettings) + "\n";
            logger.Debug(() => $"[{ConnectionId}] Sending request: {json.Trim()}");

            try
            {
                upstream.QueueWrite(Encoding.UTF8.GetBytes(json));
            }
            catch (ObjectDisposedException)
            {
                // ignored
            }
        }

        public IPEndPoint RemoteEndPoint => upstream?.GetPeerEndPoint();
        public string ConnectionId => (string) upstream?.UserToken;

        public void Close()
        {
            upstream?.Dispose();
        }

        #endregion
    }
}
