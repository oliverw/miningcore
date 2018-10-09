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
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Mining;
using Miningcore.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Stratum
{
    public class StratumClient
    {
        public StratumClient(ILogger logger, IMasterClock clock, string connectionId)
        {
            this.logger = logger;

            receivePipe = new Pipe(PipeOptions.Default);

            sendQueue = new BufferBlock<object>(new DataflowBlockOptions
            {
                BoundedCapacity = 32,
                EnsureOrdered = true,
            });

            this.clock = clock;
            ConnectionId = connectionId;
            IsAlive = true;
        }

        public StratumClient()
        {
            // For unit testing only
        }

        private readonly ILogger logger;
        private readonly IMasterClock clock;

        private const int MaxInboundRequestLength = 0x8000;
        private const int MaxOutboundRequestLength = 0x8000;

        private Stream networkStream;
        private readonly Pipe receivePipe;
        private readonly BufferBlock<object> sendQueue;
        private WorkerContextBase context;
        private readonly Subject<Unit> terminated = new Subject<Unit>();
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private bool expectingProxyHeader;

        private static readonly IPAddress IPv4LoopBackOnIPv6 = IPAddress.Parse("::ffff:127.0.0.1");

        private static readonly JsonSerializer serializer = new JsonSerializer
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private static readonly TimeSpan sendTimeout = TimeSpan.FromMilliseconds(10000);

        #region API-Surface

        public void Run(Socket socket,
            (IPEndPoint IPEndPoint, PoolEndpoint PoolEndpoint) poolEndpoint,
            X509Certificate2 tlsCert,
            Func<StratumClient, JsonRpcRequest, CancellationToken, Task> onRequestAsync,
            Action<StratumClient> onCompleted,
            Action<StratumClient, Exception> onError)
        {
            PoolEndpoint = poolEndpoint.IPEndPoint;
            RemoteEndpoint = (IPEndPoint)socket.RemoteEndPoint;

            expectingProxyHeader = poolEndpoint.PoolEndpoint.TcpProxyProtocol?.Enable == true;

            Task.Run(async () =>
            {
                try
                {
                    // prepare socket
                    socket.NoDelay = true;
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                    // create stream
                    networkStream = new NetworkStream(socket, true);

                    using (new CompositeDisposable(networkStream, cts))
                    {
                        // TLS handshake
                        if (poolEndpoint.PoolEndpoint.Tls)
                        {
                            var sslStream = new SslStream(networkStream, false);
                            await sslStream.AuthenticateAsServerAsync(tlsCert, false, SslProtocols.Tls11 | SslProtocols.Tls12, false);

                            networkStream = sslStream;
                            logger.Info(() => $"[{ConnectionId}] {sslStream.SslProtocol.ToString().ToUpper()}-{sslStream.CipherAlgorithm.ToString().ToUpper()} Connection from {RemoteEndpoint.Address}:{RemoteEndpoint.Port} accepted on port {poolEndpoint.IPEndPoint.Port}");
                        }

                        else
                            logger.Info(() => $"[{ConnectionId}] Connection from {RemoteEndpoint.Address}:{RemoteEndpoint.Port} accepted on port {poolEndpoint.IPEndPoint.Port}");

                        // Async I/O loop(s)
                        var tasks = new[]
                        {
                            FillReceivePipeAsync(),
                            ProcessReceivePipeAsync(poolEndpoint.PoolEndpoint.TcpProxyProtocol, onRequestAsync),
                            ProcessSendQueueAsync()
                        };

                        await Task.WhenAny(tasks);

                        // We are done with this client, make sure all tasks complete
                        receivePipe.Reader.Complete();
                        receivePipe.Writer.Complete();
                        sendQueue.Complete();

                        // additional safety net to ensure remaining tasks don't linger
                        cts.Cancel();

                        // Signal completion or error
                        var error = tasks.FirstOrDefault(t => t.IsFaulted)?.Exception;

                        if (error == null)
                            onCompleted(this);
                        else
                            onError(this, error);
                    }
                }

                catch (Exception ex)
                {
                    onError(this, ex);
                }

                finally
                {
                    // Release external observables
                    IsAlive = false;
                    terminated.OnNext(Unit.Default);

                    logger.Info(() => $"[{ConnectionId}] Connection closed");
                }
            });
        }

        public string ConnectionId { get; }
        public IPEndPoint PoolEndpoint { get; private set; }
        public IPEndPoint RemoteEndpoint { get; private set; }
        public DateTime? LastReceive { get; set; }
        public bool IsAlive { get; set; }
        public IObservable<Unit> Terminated => terminated.AsObservable();

        public void SetContext<T>(T value) where T : WorkerContextBase
        {
            context = value;
        }

        public T ContextAs<T>() where T : WorkerContextBase
        {
            return (T)context;
        }

        public Task RespondAsync<T>(T payload, object id)
        {
            Contract.RequiresNonNull(payload, nameof(payload));
            Contract.RequiresNonNull(id, nameof(id));

            return RespondAsync(new JsonRpcResponse<T>(payload, id));
        }

        public Task RespondErrorAsync(StratumError code, string message, object id, object result = null, object data = null)
        {
            Contract.RequiresNonNull(message, nameof(message));

            return RespondAsync(new JsonRpcResponse(new JsonRpcException((int)code, message, null), id, result));
        }

        public Task RespondAsync<T>(JsonRpcResponse<T> response)
        {
            Contract.RequiresNonNull(response, nameof(response));

            return SendAsync(response);
        }

        public Task NotifyAsync<T>(string method, T payload)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            return NotifyAsync(new JsonRpcRequest<T>(method, payload, null));
        }

        public Task NotifyAsync<T>(JsonRpcRequest<T> request)
        {
            Contract.RequiresNonNull(request, nameof(request));

            return SendAsync(request);
        }

        public void Disconnect()
        {
            networkStream.Close();
        }

        #endregion // API-Surface

        private T Deserialize<T>(ReadOnlySequence<byte> line)
        {
            // zero-copy fast-path
            //if (line.IsSingleSegment)
            //{
            //    var span = line.First.Span;

            //    fixed (byte* buf = span)
            //    {
            //        using (var stream = new UnmanagedMemoryStream(buf, span.Length))
            //        {
            //            using (var jr = new JsonTextReader(new StreamReader(stream, StratumConstants.Encoding)))
            //            {
            //                return serializer.Deserialize<T>(jr);
            //            }
            //        }
            //    }
            //}

            // slow path
            using (var jr = new JsonTextReader(new StreamReader(new MemoryStream(line.ToArray()), StratumConstants.Encoding)))
            {
                return serializer.Deserialize<T>(jr);
            }
        }

        private async Task SendAsync<T>(T payload)
        {
            if (!await sendQueue.SendAsync(payload, cts.Token))
                logger.Warn(() => $"[{ConnectionId}] Failed to queue response");
        }

        private async Task FillReceivePipeAsync()
        {
            while (true)
            {
                var memory = receivePipe.Writer.GetMemory(MaxInboundRequestLength + 1);

                var cb = await networkStream.ReadAsync(memory, cts.Token);
                if (cb == 0)
                    break; // EOF

                LastReceive = clock.Now;
                receivePipe.Writer.Advance(cb);

                var result = await receivePipe.Writer.FlushAsync(cts.Token);

                if (result.IsCompleted)
                    break;
            }
        }

        private async Task ProcessReceivePipeAsync(TcpProxyProtocolConfig proxyProtocol,
            Func<StratumClient, JsonRpcRequest, CancellationToken, Task> onRequestAsync)
        {
            while (true)
            {
                var result = await receivePipe.Reader.ReadAsync(cts.Token);

                var buffer = result.Buffer;
                SequencePosition? position = null;

                if (buffer.Length > MaxInboundRequestLength)
                    throw new InvalidDataException($"Incoming data exceeds maximum of {MaxInboundRequestLength}");

                do
                {
                    // Scan buffer for line terminator
                    position = buffer.PositionOf((byte)'\n');

                    if (position != null)
                    {
                        var slice = buffer.Slice(0, position.Value);

                        logger.Trace(() => $"[{ConnectionId}] Received data: {slice.AsString(StratumConstants.Encoding)}");

                        if (!expectingProxyHeader || !ProcessProxyHeader(slice, proxyProtocol))
                            await ProcessRequestAsync(onRequestAsync, slice);

                        // Skip consumed section
                        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
                    }
                } while (position != null);

                receivePipe.Reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                    break;
            }
        }

        private async Task ProcessSendQueueAsync()
        {
            while (true)
            {
                var payload = await sendQueue.ReceiveAsync(cts.Token);

                await SendResponse(payload);
            }
        }

        private async Task SendResponse(object payload)
        {
            logger.Trace(() => $"[{ConnectionId}] Sending: {JsonConvert.SerializeObject(payload)}");

            using(var ctsTimeout = new CancellationTokenSource())
            {
                using(var ctsComposite = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ctsTimeout.Token))
                {
                    // serialize to JSON
                    using(var writer = new StreamWriter(networkStream, StratumConstants.Encoding, MaxOutboundRequestLength, true))
                    {
                        serializer.Serialize(writer, payload);
                    }

                    // append terminator
                    networkStream.WriteByte(0xa);

                    // Send to network
                    ctsTimeout.CancelAfter(sendTimeout);
                    await networkStream.FlushAsync(ctsComposite.Token);
                }
            }
        }

        private async Task ProcessRequestAsync(
            Func<StratumClient, JsonRpcRequest, CancellationToken, Task> onRequestAsync,
            ReadOnlySequence<byte> lineBuffer)
        {
            var request = Deserialize<JsonRpcRequest>(lineBuffer);

            if (request == null)
                throw new JsonException("Unable to deserialize request");

            await onRequestAsync(this, request, cts.Token);
        }

        /// <summary>
        /// Returns true if the line was consumed
        /// </summary>
        private bool ProcessProxyHeader(ReadOnlySequence<byte> seq, TcpProxyProtocolConfig proxyProtocol)
        {
            expectingProxyHeader = false;

            var line = seq.AsString(StratumConstants.Encoding);
            var peerAddress = RemoteEndpoint.Address;

            if (line.StartsWith("PROXY "))
            {
                var proxyAddresses = proxyProtocol.ProxyAddresses?.Select(x => IPAddress.Parse(x)).ToArray();
                if (proxyAddresses == null || !proxyAddresses.Any())
                    proxyAddresses = new[] { IPAddress.Loopback, IPv4LoopBackOnIPv6, IPAddress.IPv6Loopback };

                if (proxyAddresses.Any(x => x.Equals(peerAddress)))
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
                    throw new InvalidDataException($"[{ConnectionId}] Received spoofed Proxy-Protocol header from {peerAddress}");
                }

                return true;
            }

            else if (proxyProtocol.Mandatory)
            {
                throw new InvalidDataException($"[{ConnectionId}] Missing mandatory Proxy-Protocol header from {peerAddress}. Closing connection.");
            }

            return false;
        }
    }
}
