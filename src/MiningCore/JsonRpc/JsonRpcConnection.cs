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
    public interface IJsonRpcConnection
    {
        IObservable<Timestamped<JsonRpcRequest>> Received { get; }
        string ConnectionId { get; }
        void Send<T>(T payload);
    }

    public class JsonRpcConnection : IJsonRpcConnection
    {
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private static readonly JsonSerializer serializer = new JsonSerializer
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private static readonly RecyclableMemoryStreamManager streamManager = new RecyclableMemoryStreamManager(0x200, 16 * 0x200, 0x20000);

        private const int MaxRequestLength = 8192;
        private static readonly Encoding encoding = Encoding.ASCII;
        private ConcurrentQueue<RecyclableMemoryStream> sendQueue;
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
            sendQueue = new ConcurrentQueue<RecyclableMemoryStream>();
            sendQueueDrainer = loop.CreateAsync(DrainSendQueue);
            sendQueueDrainer.UserToken = tcp;

            var incomingLines = Observable.Create<RecyclableMemoryStream>(observer =>
            {
                var stm = (RecyclableMemoryStream)streamManager.GetStream();

                tcp.OnRead((handle, buffer) =>
                {
                    // onAccept
                    using (buffer)
                    {
                        var count = buffer.Count;
                        var buf = PooledBuffers.Bytes.Rent(count);

                        // clear left-over contents
                        Array.Clear(buf, 0, count);

                        try
                        {
                            // read buffer
                            buffer.ReadBytes(buf, count);

                            while (true)
                            {
                                // check if we got a newline
                                var index = buf.IndexOf(0xa, 0, count);
                                var found = index != -1;

                                // split at newline boundaries
                                stm.Write(buf, 0, found ? index++ : count);

                                if (stm.Length > MaxRequestLength)
                                    observer.OnError(new InvalidDataException($"[{ConnectionId}] Incoming message exceeds maximum length of {MaxRequestLength}"));

                                if (!found)
                                    break;

                                // done with this stream
                                observer.OnNext(stm);
                                stm = (RecyclableMemoryStream) streamManager.GetStream();

                                // done with this packet?
                                if (index >= count - 1)
                                    break;

                                // shift buffer contents
                                var cb = count - index;
                                Array.Copy(buf, index, buf, 0, cb);
                                Array.Clear(buf, cb, count - cb);

                                count = cb;
                            }
                        }

                        finally
                        {
                            PooledBuffers.Bytes.Return(buf, true);
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
                    handle.CloseHandle();
                    sendQueueDrainer.CloseHandle();
                    sendQueueDrainer.UserToken = null;

                    // empty sendqueue
                    while (sendQueue.TryDequeue(out var data))
                        data.Dispose();

                    stm.Dispose();
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
                .Select(line =>
                {
                    line.Seek(0, SeekOrigin.Begin);

                    using (var reader = new StreamReader(line, encoding))
                    {
                        using (var jreader = new JsonTextReader(reader))
                        {
                            return serializer.Deserialize<JsonRpcRequest>(jreader);
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
                var stream = (RecyclableMemoryStream) streamManager.GetStream();

                // serialize payload
                using (var writer = new StreamWriter(stream, encoding, 0x400, true))
                    serializer.Serialize(writer, payload);

                logger.Trace(() => $"[{ConnectionId}] Sending: {encoding.GetString(stream.GetBuffer(), 0, (int) stream.Length)}");

                // append newline
                stream.WriteByte(0xa);

                SendInternal(stream);
            }
        }

        public IPEndPoint RemoteEndPoint { get; private set; }
        public string ConnectionId { get; private set; }

        #endregion

        private void SendInternal(RecyclableMemoryStream stream)
        {
            Contract.RequiresNonNull(stream, nameof(stream));

            try
            {
                sendQueue.Enqueue(stream);
                sendQueueDrainer.Send();
            }

            catch(ObjectDisposedException)
            {
                stream.Dispose();
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

                    while(sendQueue.TryDequeue(out var stream))
                    {
                        try
                        {
                            var buf = stream.GetBuffer();
                            tcp.QueueWrite(buf, 0, (int) stream.Length);
                        }

                        finally
                        {
                            // return pooled stream
                            stream.Dispose();
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
