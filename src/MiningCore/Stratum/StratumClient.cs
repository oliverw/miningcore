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
using System.Net.Sockets;
using System.Threading.Tasks;
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
        public StratumClient(Socket socket, IMasterClock clock, IPEndPoint endpointConfig, string connectionId)
        {
            this.socket = socket;
            pipe = new Pipe(PipeOptions.Default);

            this.clock = clock;
            PoolEndpoint = endpointConfig;
            ConnectionId = connectionId;
        }

        public StratumClient()
        {
            // For unit testing only
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly IMasterClock clock;

        private const int MaxInboundRequestLength = 0x8000;
        private const int MaxOutboundRequestLength = 0x8000;

        private readonly Socket socket;
        private readonly Pipe pipe;

        private bool isAlive = true;
        private WorkerContextBase context;
        private bool expectingProxyProtocolHeader = false;

        private static readonly JsonSerializer serializer = new JsonSerializer
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        #region API-Surface

        public void Run((IPEndPoint IPEndPoint, TcpProxyProtocolConfig ProxyProtocol) endpointConfig,
            Func<StratumClient, JsonRpcRequest, Task> onNext, Action<StratumClient> onCompleted, Action<StratumClient, Exception> onError)
        {
            PoolEndpoint = endpointConfig.IPEndPoint;
            RemoteEndpoint = (IPEndPoint) socket.RemoteEndPoint;

            expectingProxyProtocolHeader = endpointConfig.ProxyProtocol?.Enable == true;

            Task.Run(async () =>
            {
                try
                {
                    using(socket)
                    {
                        await Task.WhenAll(
                            FillPipeAsync(),
                            ProcessPipeAsync(endpointConfig.ProxyProtocol, onNext));

                        isAlive = false;
                        onCompleted(this);
                    }
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
                    using(var stream = new MemoryStream(buf, true))
                    {
                        stream.SetLength(0);
                        int size;

                        using(var writer = new StreamWriter(stream, StratumConstants.Encoding))
                        {
                            serializer.Serialize(writer, payload);
                            writer.Flush();

                            // append newline
                            stream.WriteByte(0xa);
                            size = (int) stream.Position;
                        }

                        logger.Trace(() => $"[{ConnectionId}] Sending: {StratumConstants.Encoding.GetString(buf, 0, size)}");

                        socket.Send(buf, size, SocketFlags.None);
                    }
                }

                catch(ObjectDisposedException)
                {
                    // ignored
                }

                finally
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }
            }
        }

        public void Disconnect()
        {
            socket.Close();

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

        public JsonRpcRequest DeserializeRequest(string json)
        {
            using (var jreader = new JsonTextReader(new StringReader(json)))
            {
                return serializer.Deserialize<JsonRpcRequest>(jreader);
            }
        }

        #endregion // API-Surface

        private async Task FillPipeAsync()
        {
            while (true)
            {
                var memory = pipe.Writer.GetMemory(MaxInboundRequestLength + 1);

                try
                {

                    var cb = await socket.ReceiveAsync(memory, SocketFlags.None);

                    if (cb == 0)
                        break;  // EOF

                    if (cb > MaxInboundRequestLength)
                        throw new InvalidDataException($"Incoming data exceeds maximum of {MaxInboundRequestLength}");

                    LastReceive = clock.Now;

                    pipe.Writer.Advance(cb);
                }

                catch (Exception)
                {
                    // Ensure that ProcessPipeAsync completes as well
                    pipe.Writer.Complete();

                    throw;
                }

                var result = await pipe.Writer.FlushAsync();

                if (result.IsCompleted)
                    break;
            }

            pipe.Writer.Complete();
        }

        private async Task ProcessPipeAsync(TcpProxyProtocolConfig proxyProtocol, Func<StratumClient, JsonRpcRequest, Task> onNext)
        {
            while(true)
            {
                var result = await pipe.Reader.ReadAsync();

                var buffer = result.Buffer;
                SequencePosition? position = null;

                do
                {
                    // Look for a EOL in the buffer
                    position = buffer.PositionOf((byte) '\n');

                    if (position != null)
                    {
                        var slice = buffer.Slice(0, position.Value);
                        var line = StratumConstants.Encoding.GetString(slice.ToArray());

                        logger.Trace(() => $"[{ConnectionId}] Received data: {line}");

                        if (!expectingProxyProtocolHeader)
                        {
                            // Process line
                            var request = DeserializeRequest(line);

                            if (request == null)
                            {
                                Disconnect();
                                throw new JsonException("Unable to deserialize request");
                            }

                            await onNext(this, request);
                        }

                        else
                        {
                            // Handle proxy header
                            if (!ProcessProxyHeader(line, proxyProtocol))
                            {
                                Disconnect();
                                break;
                            }
                        }

                        // Skip the line + the \n character (basically position)
                        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
                    }
                } while(position != null);

                pipe.Reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                    break;
            }
        }

        private bool ProcessProxyHeader(string line, TcpProxyProtocolConfig proxyProtocol)
        {
            expectingProxyProtocolHeader = false;
            var peerAddress = RemoteEndpoint.Address;

            if (line.StartsWith("PROXY "))
            {
                //var proxyAddresses = proxyProtocol.ProxyAddresses?.Select(x => IPAddress.Parse(x)).ToArray();
                //if (proxyAddresses == null || !proxyAddresses.Any())
                //    proxyAddresses = new[] { IPAddress.Loopback };

                //if (proxyAddresses.Any(x => x.Equals(peerAddress)))
                {
                    logger.Debug(() => $"[{ConnectionId}] Received Proxy-Protocol header: {line}");

                    // split header parts
                    var parts = line.Split(" ");
                    var remoteAddress = parts[2];
                    var remotePort = parts[4];

                    // Update client
                    RemoteEndpoint = new IPEndPoint(IPAddress.Parse(remoteAddress), int.Parse(remotePort));
                    logger.Info(() => $"[{ConnectionId}] Real-IP via Proxy-Protocol: {RemoteEndpoint.Address}");

                    return true;
                }

                //else
                //{
                //    logger.Error(() => $"[{ConnectionId}] Received spoofed Proxy-Protocol header from {peerAddress}");
                //    return false;
                //}
            }

            if (proxyProtocol.Mandatory)
            {
                logger.Error(() => $"[{ConnectionId}] Missing mandatory Proxy-Protocol header from {peerAddress}. Closing connection.");
            }

            return false;
        }
    }
}
