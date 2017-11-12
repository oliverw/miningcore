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
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using Microsoft.IO;
using MiningCore.Buffers;
using MiningCore.Extensions;
using NetUV.Core.Handles;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using Contract = MiningCore.Contracts.Contract;

// http://www.jsonrpc.org/specification
// https://github.com/Astn/JSON-RPC.NET

namespace MiningCore.JsonRpc
{
    public class JsonRpcConnection
    {
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private static readonly JsonSerializer serializer = new JsonSerializer
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private static readonly RecyclableMemoryStreamManager streamManager = new RecyclableMemoryStreamManager(0x200, 16 * 0x200, 0x20000);

        private const int MaxRequestLength = 8192;
        private static readonly Encoding Encoding = Encoding.ASCII;
        private static readonly ArrayPool<byte> ByteArrayPool = ArrayPool<byte>.Shared;

        private ConcurrentQueue<PooledArraySegment<byte>> sendQueue;
        private Async sendQueueDrainer;
        private bool isAlive = true;

        #region Implementation of IJsonRpcConnection

        public void Init(Loop loop, Tcp tcp, string connectionId)
        {
            Contract.RequiresNonNull(tcp, nameof(tcp));

            // cached properties
            ConnectionId = connectionId;
            RemoteEndPoint = tcp.GetPeerEndPoint();

            // initialize send queue
            sendQueue = new ConcurrentQueue<PooledArraySegment<byte>>();
            sendQueueDrainer = loop.CreateAsync(DrainSendQueue);
            sendQueueDrainer.UserToken = tcp;

            var incomingLines = Observable.Create<PooledArraySegment<byte>>(observer =>
            {
                var recvQueue = new Queue<PooledArraySegment<byte>>();

                tcp.OnRead((handle, buffer) =>
                {
                    // onAccept
                    using (buffer)
                    {
                        var count = buffer.Count;
                        if (count == 0)
                            return;

                        var buf = ByteArrayPool.Rent(count);
                        var prevIndex = 0;
                        var keepLease = false;

                        try
                        {
                            // clear left-over contents
                            if(buf.Length > count)
                                Array.Clear(buf, count, buf.Length - count);

                            // read buffer
                            buffer.ReadBytes(buf, count);

                            while (count > 0)
                            {
                                // check if we got a newline
                                var index = buf.IndexOf(0xa, prevIndex, count);
                                var found = index != -1;

                                if (found)
                                {
                                    // fastpath
                                    if (index + 1 == count && recvQueue.Count == 0)
                                    {
                                        observer.OnNext(new PooledArraySegment<byte>(buf, 0, index));
                                        keepLease = true;
                                        break;
                                    }

                                    // build buffer
                                    var queuedLength = recvQueue.Sum(x => x.Size);
                                    var lineLength = queuedLength + index;
                                    var line = ByteArrayPool.Rent(lineLength);
                                    var offset = 0;

                                    while(recvQueue.TryDequeue(out var segment))
                                    {
                                        using(segment)
                                        {
                                            Array.Copy(segment.Array, 0, line, offset, segment.Size);
                                            offset += segment.Size;
                                        }
                                    }

                                    // append latest buffer
                                    Array.Copy(buf, 0, line, offset, index);

                                    // emit
                                    observer.OnNext(new PooledArraySegment<byte>(line, 0, lineLength));

                                    prevIndex = index + 1;
                                    count -= prevIndex;
                                    continue;
                                }

                                // store
                                if (prevIndex != 0)
                                {
                                    var fragmentLength = count - prevIndex;

                                    if (fragmentLength > 0)
                                    {
                                        var fragment = ByteArrayPool.Rent(fragmentLength);
                                        Array.Copy(buf, prevIndex, fragment, 0, fragmentLength);
                                        recvQueue.Enqueue(new PooledArraySegment<byte>(fragment, 0, fragmentLength));
                                    }
                                }

                                else
                                {
                                    recvQueue.Enqueue(new PooledArraySegment<byte>(buf, 0, count));
                                    keepLease = true;
                                }

                                // prevent flooding
                                if (recvQueue.Sum(x => x.Size) > MaxRequestLength)
                                    throw new InvalidDataException($"[{ConnectionId}] Incoming message exceeds maximum length of {MaxRequestLength}");

                                break;
                            }
                        }

                        finally
                        {
                            if(!keepLease)
                                ByteArrayPool.Return(buf);
                        }
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
                    handle.Dispose();
                    sendQueueDrainer.UserToken = null;
                    sendQueueDrainer.Dispose();

                    // empty queues
                    while (sendQueue.TryDequeue(out var fragment))
                        fragment.Dispose();

                    while (recvQueue.TryDequeue(out var fragment))
                        fragment.Dispose();
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
                .Select(segment =>
                {
                    using(segment)
                    {
                        using(var stream = new MemoryStream(segment.Array, 0, segment.Size))
                        {
                            using(var reader = new StreamReader(stream, Encoding))
                            {
                                using(var jreader = new JsonTextReader(reader))
                                {
                                    return serializer.Deserialize<JsonRpcRequest>(jreader);
                                }
                            }
                        }
                    }
                })
                .Timestamp()
                .Publish()
                .RefCount();
        }

        public IObservable<Timestamped<JsonRpcRequest>> Received { get; private set; }

        public void Send<T>(T payload)
        {
            Contract.RequiresNonNull(payload, nameof(payload));

            if (isAlive)
            {
                using(var stream = (RecyclableMemoryStream) streamManager.GetStream())
                {
                    using(var writer = new StreamWriter(stream, Encoding, 0x400, true))
                    {
                        serializer.Serialize(writer, payload);
                    }

                    // append newline
                    stream.WriteByte(0xa);

                    // log it
                    logger.Trace(() => $"[{ConnectionId}] Sending: {Encoding.GetString(stream.GetBuffer(), 0, (int)stream.Length)}");

                    // copy buffer and queue up
                    var buf = ByteArrayPool.Rent((int) stream.Length);
                    Array.Copy(stream.GetBuffer(), buf, stream.Length);

                    SendInternal(new PooledArraySegment<byte>(buf, 0, (int) stream.Length));
                }
            }
        }

        public IPEndPoint RemoteEndPoint { get; private set; }
        public string ConnectionId { get; private set; }

        #endregion

        private void SendInternal(PooledArraySegment<byte> buffer)
        {
            Contract.RequiresNonNull(buffer, nameof(buffer));

            try
            {
                sendQueue.Enqueue(buffer);
                sendQueueDrainer.Send();
            }

            catch(ObjectDisposedException)
            {
                buffer.Dispose();
            }
        }

        private void DrainSendQueue(Async handle)
        {
            try
            {
                var tcp = (Tcp) handle.UserToken;

                if (tcp?.IsValid == true && !tcp.IsClosing && tcp.IsWritable && sendQueue != null)
                {
                    var queueSize = sendQueue.Count;
                    if (queueSize >= 256)
                        logger.Warn(()=> $"[{ConnectionId}] Send queue backlog now at {queueSize}");

                    while(sendQueue.TryDequeue(out var segment))
                    {
                        using(segment)
                        {
                            tcp.QueueWrite(segment.Array, 0, segment.Size);
                        }
                    }
                }
            }

            catch(Exception ex)
            {
                logger.Error(ex);
            }
        }
    }
}
