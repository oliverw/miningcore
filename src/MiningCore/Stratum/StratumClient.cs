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
using System.Reactive.Disposables;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.JsonRpc;
using MiningCore.Mining;
using MiningCore.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Stratum
{
    public class StratumClient
    {
        public StratumClient(ILogger logger, IMasterClock clock, string connectionId)
        {
            this.logger = logger;
            receivePipe = new Pipe(PipeOptions.Default);

            sendQueue = new BufferBlock<object>(new DataflowBlockOptions
            {
                BoundedCapacity = 100,
                EnsureOrdered = true,
            });

            sendQueueDFB = sendQueue;

            this.clock = clock;
            ConnectionId = connectionId;
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
        private readonly IDataflowBlock sendQueueDFB;
        private WorkerContextBase context;
        private bool expectingProxyHeader = false;

        private static readonly IPAddress IPv4LoopBackOnIPv6 = IPAddress.Parse("::ffff:127.0.0.1");

        private static readonly JsonSerializer serializer = new JsonSerializer
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        #region API-Surface

        public void Run(Socket socket,
            (IPEndPoint IPEndPoint, PoolEndpoint PoolEndpoint) poolEndpoint,
            X509Certificate2 tlsCert,
            Func<StratumClient, JsonRpcRequest, Task> onRequestAsync,
            Action<StratumClient> onCompleted,
            Action<StratumClient, Exception> onError)
        {
            PoolEndpoint = poolEndpoint.IPEndPoint;
            RemoteEndpoint = (IPEndPoint) socket.RemoteEndPoint;

            expectingProxyHeader = poolEndpoint.PoolEndpoint.TcpProxyProtocol?.Enable == true;

            Task.Run(async () =>
            {
                try
                {
                    // prepare socket
                    socket.NoDelay = true;

                    // create stream
                    networkStream = new NetworkStream(socket, true);

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

                    using(networkStream)
                    {
                        await Task.WhenAll(
                            FillReceivePipeAsync(),
                            ProcessReceivePipeAsync(poolEndpoint.PoolEndpoint.TcpProxyProtocol, onRequestAsync),
                            ProcessSendQueueAsync());

                        onCompleted(this);
                    }
                }

                catch(ObjectDisposedException)
                {
                    try
                    {
                        onCompleted(this);
                    }

                    catch (Exception ex)
                    {
                        logger.Error(ex);
                    }
                }

                catch(Exception ex)
                {
                    try
                    {
                        onError(this, ex);
                    }

                    catch (Exception ex2)
                    {
                        logger.Error(ex2);
                    }
                }

                finally
                {
                    logger.Info(() => $"[{ConnectionId}] Connection closed");
                }
            });
        }

        public string ConnectionId { get; }
        public IPEndPoint PoolEndpoint { get; private set; }
        public IPEndPoint RemoteEndpoint { get; private set; }
        public DateTime? LastReceive { get; set; }
        public CompositeDisposable Disposables { get; } = new CompositeDisposable();

        public void SetContext<T>(T value) where T : WorkerContextBase
        {
            context = value;
        }

        public T ContextAs<T>() where T : WorkerContextBase
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

        public void Disconnect()
        {
            networkStream.Close();

            Disposables.Dispose();
        }

        #endregion // API-Surface

        private unsafe T Deserialize<T>(ReadOnlySequence<byte> line)
        {
            // zero-copy fast-path
            if (line.IsSingleSegment)
            {
                var span = line.First.Span;

                fixed (byte* buf = span)
                {
                    using (var stream = new UnmanagedMemoryStream(buf, span.Length))
                    {
                        using (var jr = new JsonTextReader(new StreamReader(stream, StratumConstants.Encoding)))
                        {
                            return serializer.Deserialize<T>(jr);
                        }
                    }

                }
            }

            // slow path
            using (var jr = new JsonTextReader(new StreamReader(new MemoryStream(line.ToArray()), StratumConstants.Encoding)))
            {
                return serializer.Deserialize<T>(jr);
            }
        }

        private string GetString(ReadOnlySequence<byte> line)
        {
            return StratumConstants.Encoding.GetString(line.ToSpan());
        }

        private void Send<T>(T payload)
        {
            sendQueue.Post(payload);
        }

        private async Task FillReceivePipeAsync()
        {
            while(true)
            {
                var memory = receivePipe.Writer.GetMemory(MaxInboundRequestLength + 1);

                try
                {
                    var cb = await networkStream.ReadAsync(memory);
                    if (cb == 0)
                        break; // EOF

                    LastReceive = clock.Now;
                    receivePipe.Writer.Advance(cb);
                }

                catch(Exception)
                {
                    // Ensure that ProcessReceivePipeAsync completes as well
                    receivePipe.Writer.Complete();

                    // Ensure that ProcessSendQueue completes as well
                    sendQueue.Complete();

                    // we are done here
                    throw;
                }

                var result = await receivePipe.Writer.FlushAsync();

                if (result.IsCompleted)
                    break;
            }

            receivePipe.Writer.Complete();
            sendQueue.Complete();
        }

        private async Task ProcessReceivePipeAsync(TcpProxyProtocolConfig proxyProtocol,
            Func<StratumClient, JsonRpcRequest, Task> onRequestAsync)
        {
            while(true)
            {
                var result = await receivePipe.Reader.ReadAsync();

                var buffer = result.Buffer;
                SequencePosition? position = null;

                if (buffer.Length > MaxInboundRequestLength)
                    throw new InvalidDataException($"Incoming data exceeds maximum of {MaxInboundRequestLength}");

                do
                {
                    // Scan buffer for line terminator
                    position = buffer.PositionOf((byte) '\n');

                    if (position != null)
                    {
                        var slice = buffer.Slice(0, position.Value);

                        logger.Trace(() => $"[{ConnectionId}] Received data: {GetString(slice)}");

                        if (!expectingProxyHeader || !ProcessProxyHeader(slice, proxyProtocol))
                            await ProcessRequestAsync(onRequestAsync, slice);

                        // Skip consumed section
                        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
                    }
                } while(position != null);

                receivePipe.Reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                    break;
            }
        }

        private async Task ProcessRequestAsync(Func<StratumClient, JsonRpcRequest, Task> onRequestAsync,
            ReadOnlySequence<byte> lineBuffer)
        {
            var request = Deserialize<JsonRpcRequest>(lineBuffer);

            if (request == null)
                throw new JsonException("Unable to deserialize request");

            await onRequestAsync(this, request);
        }

        private async Task ProcessSendQueueAsync()
        {
            while (true)
            {
                var payload = await sendQueue.ReceiveAsync();

                logger.Trace(() => $"[{ConnectionId}] Sending: {JsonConvert.SerializeObject(payload)}");

                using (var writer = new StreamWriter(networkStream, StratumConstants.Encoding, MaxOutboundRequestLength, true))
                {
                    serializer.Serialize(writer, payload);
                }

                networkStream.WriteByte(0xa);  // terminator

                await networkStream.FlushAsync();
            }
        }

        /// <summary>
        /// Returns true if the line was consumed
        /// </summary>
        private bool ProcessProxyHeader(ReadOnlySequence<byte> lineBuffer, TcpProxyProtocolConfig proxyProtocol)
        {
            expectingProxyHeader = false;

            var line = GetString(lineBuffer);
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
