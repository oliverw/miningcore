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
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using Autofac;
using MiningCore.Buffers;
using MiningCore.Extensions;
using MiningCore.JsonRpc;
using MiningCore.Util;
using NetUV.Core.Handles;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Stratum
{
    public class StratumClient<TContext>
    {
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private const int MaxInboundRequestLength = 8192;
        private const int MaxOutboundRequestLength = 0x4000;

        public static readonly Encoding Encoding = new UTF8Encoding(false);
        private ConcurrentQueue<PooledArraySegment<byte>> sendQueue;
        private Async sendQueueDrainer;
        private bool isAlive = true;

        #region API-Surface

        public void Init(Loop loop, Tcp tcp, IComponentContext ctx, IPEndPoint endpointConfig, string connectionId)
        {
            Contract.RequiresNonNull(tcp, nameof(tcp));
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(endpointConfig, nameof(endpointConfig));

            PoolEndpoint = endpointConfig;

            // cached properties
            ConnectionId = connectionId;
            RemoteEndpoint = tcp.GetPeerEndPoint();

            SetupConnection(loop, tcp);
        }

        public static readonly JsonSerializer Serializer = new JsonSerializer
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        public TContext Context { get; set; }
        public string ConnectionId { get; private set; }
        public IPEndPoint PoolEndpoint { get; private set; }
        public IPEndPoint RemoteEndpoint { get; private set; }
        public IDisposable Subscription { get; set; }
        public IObservable<PooledArraySegment<byte>> Received { get; private set; }

        public void Respond<T>(T payload, object id)
        {
            Contract.RequiresNonNull(payload, nameof(payload));
            Contract.RequiresNonNull(id, nameof(id));

            Respond(new JsonRpcResponse<T>(payload, id));
        }

        public void RespondError(StratumError code, string message, object id, object result = null, object data = null)
        {
            Contract.RequiresNonNull(message, nameof(message));

            Respond(new JsonRpcResponse(new JsonRpcException((int) code, message, null), id, result));
        }

        public void Respond<T>(JsonRpcResponse<T> response)
        {
            Contract.RequiresNonNull(response, nameof(response));

            Send(response);
        }

        public void Notify<T>(string method, T payload)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            Notify(new JsonRpcRequest<T>(method, payload, null));
        }

        public void Notify<T>(JsonRpcRequest<T> request)
        {
            Contract.RequiresNonNull(request, nameof(request));

            Send(request);
        }

        public void Send<T>(T payload)
        {
            Contract.RequiresNonNull(payload, nameof(payload));

            if (isAlive)
            {
                var buf = ArrayPool<byte>.Shared.Rent(MaxOutboundRequestLength);

                try
                {
                    using (var stream = new MemoryStream(buf, true))
                    {
                        stream.SetLength(0);
                        int size;

                        using (var writer = new StreamWriter(stream))
                        {
                            Serializer.Serialize(writer, payload);
                            writer.Flush();

                            // append newline
                            stream.WriteByte(0xa);
                            size = (int)stream.Position;
                        }

                        logger.Trace(() => $"[{ConnectionId}] Sending: {Encoding.GetString(buf, 0, size)}");

                        SendInternal(new PooledArraySegment<byte>(buf, 0, size));
                    }
                }

                catch (Exception)
                {
                    ArrayPool<byte>.Shared.Return(buf);
                    throw;
                }
            }
        }

        public void Disconnect()
        {
            Subscription?.Dispose();
            Subscription = null;
        }

        public void RespondError(object id, int code, string message)
        {
            Contract.RequiresNonNull(id, nameof(id));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(message), $"{nameof(message)} must not be empty");

            Respond(new JsonRpcResponse(new JsonRpcException(code, message, null), id));
        }

        public void RespondUnsupportedMethod(object id)
        {
            Contract.RequiresNonNull(id, nameof(id));

            RespondError(id, 20, "Unsupported method");
        }

        public void RespondUnauthorized(object id)
        {
            Contract.RequiresNonNull(id, nameof(id));

            RespondError(id, 24, "Unauthorized worker");
        }

        #endregion // API-Surface

        private void SetupConnection(Loop loop, Tcp tcp)
        {
            Contract.RequiresNonNull(tcp, nameof(tcp));

            // initialize send queue
            sendQueue = new ConcurrentQueue<PooledArraySegment<byte>>();
            sendQueueDrainer = loop.CreateAsync(DrainSendQueue);
            sendQueueDrainer.UserToken = tcp;

            Received = Observable.Create<PooledArraySegment<byte>>(observer =>
            {
                var plb = new PooledLineBuffer(logger, MaxInboundRequestLength);

                tcp.OnRead((handle, buffer) =>
                {
                    // onAccept
                    using (buffer)
                    {
                        if (buffer.Count == 0 || !isAlive)
                            return;

                        plb.Receive(buffer, buffer.Count,
                            (buf, arr, count) => buf.ReadBytes(arr, count),
                            observer.OnNext,
                            observer.OnError);
                    }
                }, (handle, ex) =>
                {
                    // onError
                    observer.OnError(ex);
                }, handle =>
                {
                    // onCompleted
                    isAlive = false;
                    observer.OnCompleted();

                    // release handles
                    sendQueueDrainer.UserToken = null;
                    sendQueueDrainer.Dispose();

                    // empty queues
                    while (sendQueue.TryDequeue(out var fragment))
                        fragment.Dispose();

                    plb.Dispose();

                    handle.CloseHandle();
                });

                return Disposable.Create(() =>
                {
                    if (tcp.IsValid)
                    {
                        logger.Debug(() => $"[{ConnectionId}] Last subscriber disconnected from receiver stream");

                        isAlive = false;
                        tcp.Shutdown();
                    }
                });
            })
            .Publish()
            .RefCount();
        }

        private void SendInternal(PooledArraySegment<byte> buffer)
        {
            try
            {
                sendQueue.Enqueue(buffer);
                sendQueueDrainer.Send();
            }

            catch (ObjectDisposedException)
            {
                buffer.Dispose();
            }
        }

        private void DrainSendQueue(Async handle)
        {
            try
            {
                var tcp = (Tcp)handle.UserToken;

                if (tcp?.IsValid == true && !tcp.IsClosing && tcp.IsWritable && sendQueue != null)
                {
                    var queueSize = sendQueue.Count;
                    if (queueSize >= 256)
                        logger.Warn(() => $"[{ConnectionId}] Send queue backlog now at {queueSize}");

                    while (sendQueue.TryDequeue(out var segment))
                    {
                        using (segment)
                        {
                            tcp.QueueWrite(segment.Array, 0, segment.Size);
                        }
                    }
                }
            }

            catch (Exception ex)
            {
                logger.Error(ex);
            }
        }
    }
}
