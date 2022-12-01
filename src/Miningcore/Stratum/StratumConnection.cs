using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks.Dataflow;
using Microsoft.IO;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Mining;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Stratum;

public class StratumConnection
{
    public StratumConnection(ILogger logger, RecyclableMemoryStreamManager rmsm, IMasterClock clock, string connectionId, bool gpdrCompliantLogging)
    {
        this.logger = logger;
        this.rmsm = rmsm;

        receivePipe = new Pipe(PipeOptions.Default);

        sendQueue = new BufferBlock<object>(new DataflowBlockOptions
        {
            EnsureOrdered = true,
        });

        this.clock = clock;
        ConnectionId = connectionId;
        IsAlive = true;
        this.gpdrCompliantLogging = gpdrCompliantLogging;
    }

    private readonly ILogger logger;
    private readonly RecyclableMemoryStreamManager rmsm;
    private readonly IMasterClock clock;

    private const int MaxInboundRequestLength = 0x8000;
    public static readonly Encoding Encoding = new UTF8Encoding(false);

    private Stream networkStream;
    private readonly Pipe receivePipe;
    private readonly BufferBlock<object> sendQueue;
    private WorkerContextBase context;
    private readonly Subject<Unit> terminated = new();
    private bool expectingProxyHeader;
    private bool gpdrCompliantLogging;

    private static readonly JsonSerializer serializer = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    };

    private const int SendQueueCapacity = 16;
    private static readonly TimeSpan sendTimeout = TimeSpan.FromMilliseconds(5000);

    #region API-Surface

    public async void DispatchAsync(Socket socket, CancellationToken ct,
        StratumEndpoint endpoint, IPEndPoint remoteEndpoint, X509Certificate2 cert,
        Func<StratumConnection, JsonRpcRequest, CancellationToken, Task> onRequestAsync,
        Action<StratumConnection> onCompleted,
        Action<StratumConnection, Exception> onError)
    {
        LocalEndpoint = endpoint.IPEndPoint;
        RemoteEndpoint = remoteEndpoint;

        expectingProxyHeader = endpoint.PoolEndpoint.TcpProxyProtocol?.Enable == true;

        try
        {
            // prepare socket
            socket.NoDelay = true;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            // create stream
            networkStream = new NetworkStream(socket, true);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            using(var disposables = new CompositeDisposable(networkStream))
            {
                var tls = endpoint.PoolEndpoint.Tls;

                // auto-detect SSL
                if(endpoint.PoolEndpoint.TlsAuto)
                    tls = await DetectSslHandshake(socket, cts.Token);

                if(tls)
                {
                    var sslStream = new SslStream(networkStream, false);
                    disposables.Add(sslStream);

                    // TLS handshake
                    await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = cert,
                        ClientCertificateRequired = false,
                        EnabledSslProtocols = SslProtocols.Tls11 | SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    }, cts.Token);

                    networkStream = sslStream;

                    logger.Info(() => $"[{ConnectionId}] {sslStream.SslProtocol.ToString().ToUpper()}-{sslStream.CipherAlgorithm.ToString().ToUpper()} Connection from {RemoteEndpoint.Address.CensorOrReturn(gpdrCompliantLogging)}:{RemoteEndpoint.Port} accepted on port {endpoint.IPEndPoint.Port}");
                }
                else
                    logger.Info(() => $"[{ConnectionId}] Connection from {RemoteEndpoint.Address.CensorOrReturn(gpdrCompliantLogging)}:{RemoteEndpoint.Port} accepted on port {endpoint.IPEndPoint.Port}");

                // Async I/O loop(s)
                var tasks = new[]
                {
                    FillReceivePipeAsync(cts.Token),
                    ProcessReceivePipeAsync(cts.Token, endpoint.PoolEndpoint.TcpProxyProtocol, onRequestAsync),
                    ProcessSendQueueAsync(cts.Token)
                };

                await Task.WhenAny(tasks);

                // We are done with this client, make sure all tasks complete
                await receivePipe.Reader.CompleteAsync();
                await receivePipe.Writer.CompleteAsync();
                sendQueue.Complete();

                // additional safety net to ensure remaining tasks don't linger
                cts.Cancel();

                // Signal completion or error
                var error = tasks.FirstOrDefault(t => t.IsFaulted)?.Exception;

                if(error == null)
                    onCompleted(this);
                else
                    onError(this, error);
            }
        }

        catch(Exception ex)
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
    }

    public string ConnectionId { get; }
    public IPEndPoint LocalEndpoint { get; private set; }
    public IPEndPoint RemoteEndpoint { get; private set; }
    public DateTime? LastReceive { get; set; }
    public bool IsAlive { get; set; }
    public IObservable<Unit> Terminated => terminated.AsObservable();
    public WorkerContextBase Context => context;

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
        return RespondAsync(new JsonRpcResponse<T>(payload, id));
    }

    public Task RespondErrorAsync(StratumError code, string message, object id, object result = null)
    {
        return RespondAsync(new JsonRpcResponse(new JsonRpcError((int) code, message, null), id, result));
    }

    public Task RespondAsync<T>(JsonRpcResponse<T> response)
    {
        return SendAsync(response);
    }

    public Task NotifyAsync<T>(string method, T payload)
    {
        return NotifyAsync(new JsonRpcRequest<T>(method, payload, null));
    }

    public Task NotifyAsync<T>(JsonRpcRequest<T> request)
    {
        return SendAsync(request);
    }

    public void Disconnect()
    {
        networkStream.Close();
    }

    #endregion // API-Surface

    private Task SendAsync<T>(T payload)
    {
        Contract.RequiresNonNull(payload);

        if(sendQueue.Count >= SendQueueCapacity)
            throw new IOException("Sendqueue stalled");

        return sendQueue.SendAsync(payload);
    }

    private async Task FillReceivePipeAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            logger.Debug(() => $"[{ConnectionId}] [NET] Waiting for data ...");

            var memory = receivePipe.Writer.GetMemory(MaxInboundRequestLength + 1);

            // read from network directly into pipe memory
            var cb = await networkStream.ReadAsync(memory, ct);
            if(cb == 0)
                break; // EOF

            logger.Debug(() => $"[{ConnectionId}] [NET] Received data: {Encoding.GetString(memory.Slice(0, cb).Span)}");

            LastReceive = clock.Now;

            // hand off to pipe
            receivePipe.Writer.Advance(cb);

            var result = await receivePipe.Writer.FlushAsync(ct);
            if(result.IsCompleted)
                break;
        }
    }

    private async Task ProcessReceivePipeAsync(CancellationToken ct,
        TcpProxyProtocolConfig proxyProtocol,
        Func<StratumConnection, JsonRpcRequest, CancellationToken, Task> onRequestAsync)
    {
        while(!ct.IsCancellationRequested)
        {
            logger.Debug(() => $"[{ConnectionId}] [PIPE] Waiting for data ...");

            var result = await receivePipe.Reader.ReadAsync(ct);

            var buffer = result.Buffer;
            SequencePosition? position;

            if(buffer.Length > MaxInboundRequestLength)
                throw new InvalidDataException($"Incoming data exceeds maximum of {MaxInboundRequestLength}");

            logger.Debug(() => $"[{ConnectionId}] [PIPE] Received data: {result.Buffer.AsString(Encoding)}");

            do
            {
                // Scan buffer for line terminator
                position = buffer.PositionOf((byte) '\n');

                if(position != null)
                {
                    var slice = buffer.Slice(0, position.Value);

                    if(!expectingProxyHeader || !ProcessProxyHeader(slice, proxyProtocol))
                        await ProcessRequestAsync(ct, onRequestAsync, slice);

                    // Skip consumed section
                    buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
                }
            } while(position != null);

            receivePipe.Reader.AdvanceTo(buffer.Start, buffer.End);

            if(result.IsCompleted)
                break;
        }
    }

    private async Task<bool> DetectSslHandshake(Socket socket, CancellationToken ct)
    {
        // https://tls.ulfheim.net/
        // https://tls13.ulfheim.net/

        const int BufSize = 1;
        var buf = ArrayPool<byte>.Shared.Rent(BufSize);

        try
        {
            var cb = await socket.ReceiveAsync(buf.AsMemory()[..BufSize], SocketFlags.Peek, ct);

            if(cb == 0)
                return false;   // End of stream

            if(cb < BufSize)
                throw new Exception($"Failed to peek at connection's first {BufSize} byte(s)");

            switch(buf[0])
            {
                case 0x16: // TLS 1.0 - 1.3
                    return true;
            }
        }

        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }

        return false;
    }

    private async Task ProcessSendQueueAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            if(sendQueue.Count >= SendQueueCapacity)
                throw new IOException($"Send-queue overflow at {sendQueue.Count} of {SendQueueCapacity} items");

            var msg = await sendQueue.ReceiveAsync(ct);

            await SendMessage(msg, ct);
        }
    }

    private async Task SendMessage(object msg, CancellationToken ct)
    {
        await using var stream = rmsm.GetStream(nameof(StratumConnection)) as RecyclableMemoryStream;

        // serialize
        await using (var writer = new StreamWriter(stream!, Encoding, -1, true))
        {
            serializer.Serialize(writer, msg);
        }

        logger.Debug(() => $"[{ConnectionId}] Sending: {Encoding.GetString(stream.GetReadOnlySequence())}");

        // append newline
        stream.WriteByte((byte) '\n');

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(sendTimeout);

        // send
        stream.Position = 0;
        await stream.CopyToAsync(networkStream, cts.Token);
        await networkStream.FlushAsync(cts.Token);
    }

    private async Task ProcessRequestAsync(
        CancellationToken ct,
        Func<StratumConnection, JsonRpcRequest, CancellationToken, Task> onRequestAsync,
        ReadOnlySequence<byte> lineBuffer)
    {
        await using var stream = rmsm.GetStream(nameof(StratumConnection), lineBuffer.ToSpan()) as RecyclableMemoryStream;
        using var reader = new JsonTextReader(new StreamReader(stream!, Encoding));

        var request = serializer.Deserialize<JsonRpcRequest>(reader);

        if(request == null)
            throw new JsonException("Unable to deserialize request");

        await onRequestAsync(this, request, ct);
    }

    /// <summary>
    /// Returns true if the line was consumed
    /// </summary>
    private bool ProcessProxyHeader(ReadOnlySequence<byte> seq, TcpProxyProtocolConfig proxyProtocol)
    {
        expectingProxyHeader = false;

        var line = seq.AsString(Encoding);
        var peerAddress = RemoteEndpoint.Address;

        if(line.StartsWith("PROXY "))
        {
            var proxyAddresses = proxyProtocol.ProxyAddresses?.Select(IPAddress.Parse).ToArray();
            if(proxyAddresses == null || !proxyAddresses.Any())
                proxyAddresses = new[] { IPAddress.Loopback, IPUtils.IPv4LoopBackOnIPv6, IPAddress.IPv6Loopback };

            if(proxyAddresses.Any(x => x.Equals(peerAddress)))
            {
                logger.Debug(() => $"[{ConnectionId}] Received Proxy-Protocol header: {line}");

                // split header parts
                var parts = line.Split(" ");
                var remoteAddress = parts[2];
                var remotePort = parts[4];

                // Update client
                RemoteEndpoint = new IPEndPoint(IPAddress.Parse(remoteAddress), int.Parse(remotePort));
                logger.Info(() => $"Real-IP via Proxy-Protocol: {RemoteEndpoint.Address.CensorOrReturn(gpdrCompliantLogging)}");
            }

            else
            {
                throw new InvalidDataException($"Received spoofed Proxy-Protocol header from {peerAddress}");
            }

            return true;
        }

        if(proxyProtocol.Mandatory)
        {
            throw new InvalidDataException($"Missing mandatory Proxy-Protocol header from {peerAddress}. Closing connection.");
        }

        return false;
    }
}
