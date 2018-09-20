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
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using MiningCore.Buffers;
using MiningCore.Configuration;
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

        private bool isAlive = true;
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
                        logger.Info(() => $"[{ConnectionId}] {sslStream.SslProtocol.ToString().ToUpper()} Connection ({sslStream.CipherAlgorithm.ToString().ToUpper()}) from {RemoteEndpoint.Address}:{RemoteEndpoint.Port} accepted on port {poolEndpoint.IPEndPoint.Port}");
                    }

                    else
                        logger.Info(() => $"[{ConnectionId}] Connection from {RemoteEndpoint.Address}:{RemoteEndpoint.Port} accepted on port {poolEndpoint.IPEndPoint.Port}");

                    // Go
                    using (networkStream)
                    {
                        await Task.WhenAll(
                            FillReceivePipeAsync(),
                            ProcessReceivePipeAsync(poolEndpoint.PoolEndpoint.TcpProxyProtocol, onRequestAsync));

                        isAlive = false;
                        onCompleted(this);
                    }
                }

                catch(ObjectDisposedException)
                {
                    isAlive = false;
                    onCompleted(this);
                }

                catch(Exception ex)
                {
                    isAlive = false;
                    onError(this, ex);
                }
            });
        }

        public string ConnectionId { get; }
        public IPEndPoint PoolEndpoint { get; private set; }
        public IPEndPoint RemoteEndpoint { get; private set; }
        public DateTime? LastReceive { get; set; }
        public bool IsAlive { get; set; } = true;

        public void SetContext<T>(T value) where T : WorkerContextBase
        {
            context = value;
        }

        public T ContextAs<T>() where T : WorkerContextBase
        {
            return (T) context;
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

            return RespondAsync(new JsonRpcResponse(new JsonRpcException((int) code, message, null), id, result));
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

        public async Task SendAsync<T>(T payload)
        {
            Contract.RequiresNonNull(payload, nameof(payload));

            if (isAlive)
            {
                using(var buf = Serialize(payload))
                {
                    logger.Trace(() => $"[{ConnectionId}] Sending: {StratumConstants.Encoding.GetString(buf.Array, 0, buf.Size)}");

                    try
                    {
                        await networkStream.WriteAsync(buf.Array, 0, buf.Size);
                    }

                    catch(ObjectDisposedException)
                    {
                        // ignored
                    }
                }
            }
        }

        public void Disconnect()
        {
            networkStream.Close();

            IsAlive = false;
        }

        public PooledArraySegment<byte> Serialize(object payload)
        {
            var buf = ArrayPool<byte>.Shared.Rent(MaxOutboundRequestLength);

            try
            {
                using (var stream = new MemoryStream(buf, true))
                {
                    stream.SetLength(0);

                    using (var writer = new StreamWriter(stream, StratumConstants.Encoding))
                    {
                        serializer.Serialize(writer, payload);
                        writer.Flush();

                        // append newline
                        stream.WriteByte(0xa);

                        // return buffer
                        return new PooledArraySegment<byte>(buf, 0, (int)stream.Length);
                    }
                }
            }

            catch (Exception)
            {
                ArrayPool<byte>.Shared.Return(buf);
                throw;
            }
        }

        public T Deserialize<T>(string json)
        {
            using(var jreader = new JsonTextReader(new StringReader(json)))
            {
                return serializer.Deserialize<T>(jreader);
            }
        }

        #endregion // API-Surface

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
                    throw;
                }

                var result = await receivePipe.Writer.FlushAsync();

                if (result.IsCompleted)
                    break;
            }

            receivePipe.Writer.Complete();
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
                {
                    Disconnect();
                    throw new InvalidDataException($"Incoming data exceeds maximum of {MaxInboundRequestLength}");
                }

                do
                {
                    // Look for a EOL in the buffer
                    position = buffer.PositionOf((byte) '\n');

                    if (position != null)
                    {
                        var slice = buffer.Slice(0, position.Value);
                        var line = StratumConstants.Encoding.GetString(slice.ToArray());

                        logger.Trace(() => $"[{ConnectionId}] Received data: {line}");

                        if (!expectingProxyHeader || !HandleProxyHeader(line, proxyProtocol))
                            await ProcessRequestAsync(onRequestAsync, line);

                        // Skip the line + the \n character (basically position)
                        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
                    }
                } while(position != null);

                receivePipe.Reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                    break;
            }
        }

        private async Task ProcessRequestAsync(Func<StratumClient, JsonRpcRequest, Task> onRequestAsync, string json)
        {
            var request = Deserialize<JsonRpcRequest>(json);

            if (request == null)
            {
                Disconnect();
                throw new JsonException("Unable to deserialize request");
            }

            await onRequestAsync(this, request);
        }

        /// <summary>
        /// Returns true if the line was consumed
        /// </summary>
        private bool HandleProxyHeader(string line, TcpProxyProtocolConfig proxyProtocol)
        {
            expectingProxyHeader = false;
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
