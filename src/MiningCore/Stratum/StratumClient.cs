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
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive.Disposables;
using System.Text;
using Autofac;
using MiningCore.Buffers;
using MiningCore.Configuration;
using MiningCore.JsonRpc;
using MiningCore.Mining;
using MiningCore.Time;
using MiningCore.Util;
using NetUV.Core.Handles;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Stratum
{
    public class StratumClient
    {
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private const int MaxInboundRequestLength = 0x8000;
        private const int MaxOutboundRequestLength = 0x8000;

        private ConcurrentQueue<PooledArraySegment<byte>> sendQueue;
        private Async sendQueueDrainer;
        private readonly PooledLineBuffer plb = new PooledLineBuffer(logger, MaxInboundRequestLength);
        private IDisposable subscription;
        private bool isAlive = true;
        private WorkerContextBase context;
        private bool expectingProxyProtocolHeader = false;

        private static readonly JsonSerializer serializer = new JsonSerializer
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        #region API-Surface

        public void Init(Loop loop, Tcp tcp, IComponentContext ctx, IMasterClock clock,
            (IPEndPoint IPEndPoint, TcpProxyProtocolConfig ProxyProtocol) endpointConfig, string connectionId,
            Action<PooledArraySegment<byte>> onNext, Action onCompleted, Action<Exception> onError)
        {
            PoolEndpoint = endpointConfig.IPEndPoint;
            ConnectionId = connectionId;
            RemoteEndpoint = tcp.GetPeerEndPoint();

            expectingProxyProtocolHeader = endpointConfig.ProxyProtocol?.Enable == true;

            // initialize send queue
            sendQueue = new ConcurrentQueue<PooledArraySegment<byte>>();
            sendQueueDrainer = loop.CreateAsync(DrainSendQueue);
            sendQueueDrainer.UserToken = tcp;

            // cleanup preparation
            var sub = Disposable.Create(() =>
            {
                if (tcp.IsValid)
                {
                    logger.Debug(() => $"[{ConnectionId}] Last subscriber disconnected from receiver stream");

                    isAlive = false;
                    tcp.Shutdown();
                }
            });

            // ensure subscription is disposed on loop thread
            var disposer = loop.CreateAsync((handle) =>
            {
                sub.Dispose();

                handle.Dispose();
            });

            subscription = Disposable.Create(() => { disposer.Send(); });

            // go
            Receive(tcp, endpointConfig.ProxyProtocol, clock, onNext, onCompleted, onError);
        }

        public string ConnectionId { get; private set; }
        public IPEndPoint PoolEndpoint { get; private set; }
        public IPEndPoint RemoteEndpoint { get; private set; }
        public DateTime? LastReceive { get; set; }
        public bool IsAlive { get; set; } = true;

        public void SetContext<T>(T value) where T : WorkerContextBase
        {
            context = value;
        }

        public T GetContextAs<T>() where T: WorkerContextBase
        {
            return (T) context;
        }

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

                        using (var writer = new StreamWriter(stream, StratumConstants.Encoding))
                        {
                            serializer.Serialize(writer, payload);
                            writer.Flush();

                            // append newline
                            stream.WriteByte(0xa);
                            size = (int)stream.Position;
                        }

                        logger.Trace(() => $"[{ConnectionId}] Sending: {StratumConstants.Encoding.GetString(buf, 0, size)}");

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
            subscription?.Dispose();
            subscription = null;

            IsAlive = false;
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

        public JsonRpcRequest DeserializeRequest(PooledArraySegment<byte> data)
        {
            using (var stream = new MemoryStream(data.Array, data.Offset, data.Size))
            {
                using (var reader = new StreamReader(stream, StratumConstants.Encoding))
                {
                    using (var jreader = new JsonTextReader(reader))
                    {
                        return serializer.Deserialize<JsonRpcRequest>(jreader);
                    }
                }
            }
        }

        #endregion // API-Surface

        private void Receive(Tcp tcp, TcpProxyProtocolConfig proxyProtocol, IMasterClock clock,
            Action<PooledArraySegment<byte>> onNext, Action onCompleted, Action<Exception> onError)
        {
            tcp.OnRead((handle, buffer) =>
            {
                // onAccept
                using (buffer)
                {
                    if (buffer.Count == 0 || !isAlive)
                        return;

                    LastReceive = clock.Now;

                    var onLineReceived = !expectingProxyProtocolHeader ?
                        onNext :
                        (lineData) =>
                        {
                            // are we expecting the Tcp-Proxy-Protocol header?
                            if (expectingProxyProtocolHeader)
                            {
                                expectingProxyProtocolHeader = false;
                                var peerAddress = tcp.GetPeerEndPoint().Address;

                                // peek into line data
                                var line = Encoding.ASCII.GetString(lineData.Array, lineData.Offset, lineData.Size);

                                if (line.StartsWith("PROXY "))
                                {
                                    using(lineData)
                                    {
                                        var proxyAddresses = proxyProtocol.ProxyAddresses?.Select(x => IPAddress.Parse(x)).ToArray();
                                        if (proxyAddresses == null || !proxyAddresses.Any())
                                            proxyAddresses = new[] { IPAddress.Loopback };

                                        if (proxyAddresses.Any(x=> x.Equals(peerAddress)))
                                        {
                                            logger.Debug(() => $"[{ConnectionId}] Received Proxy-Protocol header: {line}");

                                            // split header parts
                                            var parts = line.Split(" ");
                                            var remoteAddress = parts[2];
                                            var remotePort = parts[4];

                                            // Update client
                                            RemoteEndpoint = new IPEndPoint(IPAddress.Parse(remoteAddress), int.Parse(remotePort));
                                            logger.Info(() => $"[{ConnectionId}] Real-IP via Proxy-Protocol: {RemoteEndpoint.Address}");
                                        }

                                        else
                                        {
                                            logger.Error(() => $"[{ConnectionId}] Received spoofed Proxy-Protocol header from {peerAddress}");
                                            Disconnect();
                                        }
                                    }

                                    return;
                                }

                                else if (proxyProtocol.Mandatory)
                                {
                                    logger.Error(() => $"[{ConnectionId}] Missing mandatory Proxy-Protocol header from {peerAddress}. Closing connection.");
                                    lineData.Dispose();
                                    Disconnect();
                                    return;
                                }
                            }

                            // forward
                            onNext(lineData);
                        };

                    plb.Receive(buffer, buffer.Count,
                        (src, dst, count) => src.ReadBytes(dst, count),
                        onLineReceived,
                        onError);
                }
            }, (handle, ex) =>
            {
                // onError
                onError(ex);
            }, handle =>
            {
                // onCompleted
                isAlive = false;
                onCompleted();

                // release handles
                sendQueueDrainer.UserToken = null;
                sendQueueDrainer.Dispose();

                // empty queues
                while (sendQueue.TryDequeue(out var fragment))
                    fragment.Dispose();

                plb.Dispose();

                handle.CloseHandle();
            });
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
