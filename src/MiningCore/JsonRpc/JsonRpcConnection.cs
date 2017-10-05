/* 
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial 
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

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
    public interface IJsonRpcConnection
    {
        IObservable<Timestamped<JsonRpcRequest>> Received { get; }
        string ConnectionId { get; }
        void Send<T>(JsonRpcResponse<T> response);
        void Send<T>(JsonRpcRequest<T> request);
    }

    public class JsonRpcConnection : IJsonRpcConnection
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

        public void Init(Tcp upstream, string connectionId)
        {
            Contract.RequiresNonNull(upstream, nameof(upstream));

            this.upstream = upstream;
            this.ConnectionId = connectionId;

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
                                observer.OnError(new InvalidDataException($"[{ConnectionId}] Incoming message exceeds maximum length of {MaxRequestLength}"));
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

                        upstream.CloseHandle();
                    });

                    return Disposable.Create(() =>
                    {
                        if (upstream.IsValid)
                        {
                            logger.Debug(() => $"[{ConnectionId}] Last subscriber disconnected from receiver stream");

                            upstream.Shutdown();
                        }
                    });
                });

            Received = incomingLines
                .Select(line => new
                {
                    Json = line,
                    Request = JsonConvert.DeserializeObject<JsonRpcRequest>(line, serializerSettings)
                })
                .Do(x => logger.Trace(() => $"[{ConnectionId}] Received JsonRpc-Request: {x.Json}"))
                .Select(x => x.Request)
                .Timestamp()
                .Publish()
                .RefCount();
        }

        public IObservable<Timestamped<JsonRpcRequest>> Received { get; private set; }

        public void Send<T>(JsonRpcResponse<T> response)
        {
            var json = JsonConvert.SerializeObject(response, serializerSettings) + "\n";
            logger.Trace(() => $"[{ConnectionId}] Sending response: {json.Trim()}");

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
            logger.Trace(() => $"[{ConnectionId}] Sending request: {json.Trim()}");

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
        public string ConnectionId { get; private set; }

        #endregion
    }
}
