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
using System.Collections.Concurrent;
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
        void Send(object payload);
    }

    public class JsonRpcConnection : IJsonRpcConnection
    {
        public JsonRpcConnection(JsonSerializerSettings serializerSettings)
        {
            Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));

            this.serializerSettings = serializerSettings;
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private readonly JsonSerializerSettings serializerSettings;
        private const int MaxRequestLength = 8192;
        private ConcurrentQueue<byte[]> sendQueue;
        private Async sendQueueDrainer;

        #region Implementation of IJsonRpcConnection

        public void Init(Loop loop, Tcp tcp, string connectionId)
        {
            Contract.RequiresNonNull(tcp, nameof(tcp));

            // cached properties
            ConnectionId = connectionId;
            RemoteEndPoint = tcp.GetPeerEndPoint();

            // initialize send queue
            sendQueue = new ConcurrentQueue<byte[]>();
            sendQueueDrainer = loop.CreateAsync(DrainSendQueue);
            sendQueueDrainer.UserToken = tcp;

            var incomingLines = Observable.Create<string>(observer =>
            {
                var sb = new StringBuilder();

                tcp.OnRead((handle, buffer) =>
                {
                    using (buffer)
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

                                    if (line.Length > 0)
                                        observer.OnNext(line);
                                }
                            }

                            else
                            {
                                observer.OnError(new InvalidDataException($"[{ConnectionId}] Incoming message exceeds maximum length of {MaxRequestLength}"));
                            }
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

                    // release handles
                    handle.CloseHandle();
                    sendQueueDrainer.CloseHandle();
                    sendQueueDrainer.UserToken = null;

                    sb = null;
                });

                return Disposable.Create(() =>
                {
                    if (tcp.IsValid)
                    {
                        logger.Debug(() => $"[{ConnectionId}] Last subscriber disconnected from receiver stream");

                        tcp.Shutdown();
                    }
                });
            });

            Received = incomingLines
                .Do(x => logger.Trace(() => $"[{ConnectionId}] Received JsonRpc-Request: {x}"))
                .Select(line => JsonConvert.DeserializeObject<JsonRpcRequest>(line, serializerSettings))
                .Timestamp()
                .Publish()
                .RefCount();
        }

        public IObservable<Timestamped<JsonRpcRequest>> Received { get; private set; }

        public void Send(object payload)
        {
            Contract.RequiresNonNull(payload, nameof(payload));

            var json = JsonConvert.SerializeObject(payload, serializerSettings);
            logger.Trace(() => $"[{ConnectionId}] Sending: {json}");

            SendInternal(Encoding.UTF8.GetBytes(json + '\n'));
        }

        public IPEndPoint RemoteEndPoint { get; private set; }
        public string ConnectionId { get; private set; }

        #endregion

        private void SendInternal(byte[] data)
        {
            Contract.RequiresNonNull(data, nameof(data));

            try
            {
                sendQueue.Enqueue(data);
                sendQueueDrainer.Send();
            }

            catch (ObjectDisposedException)
            {
                // ignored
            }
        }

        private void DrainSendQueue(Async handle)
        {
            try
            {
                var tcp = (Tcp) handle.UserToken;

                if (tcp?.IsValid == true && !tcp.IsClosing && tcp.IsWritable && sendQueue != null)
                {
                    while (sendQueue.TryDequeue(out var data))
                        tcp.QueueWrite(data);
                }
            }

            catch (Exception ex)
            {
                logger.Error(ex);
            }
        }
    }
}
