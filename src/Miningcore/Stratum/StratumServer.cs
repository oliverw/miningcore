using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reactive;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Autofac;
using Microsoft.IO;
using Miningcore.Banning;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using NLog;
using Contract = Miningcore.Contracts.Contract;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Stratum;

public abstract class StratumServer
{
    protected StratumServer(
        IComponentContext ctx,
        IMessageBus messageBus,
        RecyclableMemoryStreamManager rmsm,
        IMasterClock clock)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(messageBus);
        Contract.RequiresNonNull(rmsm);
        Contract.RequiresNonNull(clock);

        this.ctx = ctx;
        this.messageBus = messageBus;
        this.rmsm = rmsm;
        this.clock = clock;
    }

    static StratumServer()
    {
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ignoredSocketErrors = new HashSet<int>
            {
                (int) SocketError.ConnectionReset,
                (int) SocketError.ConnectionAborted,
                (int) SocketError.OperationAborted
            };
        }

        else if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // see: http://www.virtsync.com/c-error-codes-include-errno
            ignoredSocketErrors = new HashSet<int>
            {
                104, // ECONNRESET
                125, // ECANCELED
                103, // ECONNABORTED
                110, // ETIMEDOUT
                32,  // EPIPE
            };
        }
    }

    protected readonly ConcurrentDictionary<string, StratumConnection> connections = new();
    protected static readonly ConcurrentDictionary<string, X509Certificate2> certs = new();
    protected static readonly HashSet<int> ignoredSocketErrors;

    protected static readonly MethodBase streamWriterCtor = typeof(StreamWriter).GetConstructor(
        new[] { typeof(Stream), typeof(Encoding), typeof(int), typeof(bool) });

    protected readonly IComponentContext ctx;
    protected readonly IMessageBus messageBus;
    private readonly RecyclableMemoryStreamManager rmsm;
    protected readonly IMasterClock clock;
    protected ClusterConfig clusterConfig;
    protected PoolConfig poolConfig;
    protected IBanManager banManager;
    protected ILogger logger;

    protected Task RunAsync(CancellationToken ct, params StratumEndpoint[] endpoints)
    {
        Contract.RequiresNonNull(endpoints);

        logger.Info(() => $"Stratum ports {string.Join(", ", endpoints.Select(x => $"{x.IPEndPoint.Address}:{x.IPEndPoint.Port}").ToArray())} online");

        var tasks = endpoints.Select(port =>
        {
            var server = new Socket(SocketType.Stream, ProtocolType.Tcp);
            server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            server.Bind(port.IPEndPoint);
            server.Listen();

            return Listen(server, port, ct);
        }).ToArray();

        return Task.WhenAll(tasks);
    }

    private async Task Listen(Socket server, StratumEndpoint port, CancellationToken ct)
    {
        var cert = GetTlsCert(port);

        while(!ct.IsCancellationRequested)
        {
            try
            {
                var socket = await server.AcceptAsync(ct);

                AcceptConnection(socket, port, cert, ct);
            }

            catch(OperationCanceledException)
            {
                // ignored
                break;
            }

            catch(ObjectDisposedException)
            {
                // ignored
                break;
            }

            catch(Exception ex)
            {
                logger.Error(ex);
            }
        }
    }

    private void AcceptConnection(Socket socket, StratumEndpoint port, X509Certificate2 cert, CancellationToken ct)
    {
        Task.Run(() => Guard(() =>
        {
            var remoteEndpoint = (IPEndPoint) socket.RemoteEndPoint;

            if(remoteEndpoint == null)
            {
                socket.Close();
                return;
            }

            // dispose of banned clients as early as possible
            if (DisconnectIfBanned(socket, remoteEndpoint))
                return;

            // init connection
            var connection = new StratumConnection(logger, rmsm, clock, CorrelationIdGenerator.GetNextId(), clusterConfig.Logging.GPDRCompliant);

            logger.Info(() => $"[{connection.ConnectionId}] Accepting connection from {remoteEndpoint.Address.CensorOrReturn(clusterConfig.Logging.GPDRCompliant)}:{remoteEndpoint.Port} ...");

            RegisterConnection(connection);
            OnConnect(connection, port.IPEndPoint);

            connection.DispatchAsync(socket, ct, port, remoteEndpoint, cert, OnRequestAsync, OnConnectionComplete, OnConnectionError);
        }, ex=> logger.Error(ex)), ct);
    }

    protected void RegisterConnection(StratumConnection connection)
    {
        var result = connections.TryAdd(connection.ConnectionId, connection);
        Debug.Assert(result);

        PublishTelemetry(TelemetryCategory.Connections, TimeSpan.Zero, true, connections.Count);
    }

    protected void UnregisterConnection(StratumConnection connection)
    {
        var result = connections.TryRemove(connection.ConnectionId, out _);
        Debug.Assert(result);

        PublishTelemetry(TelemetryCategory.Connections, TimeSpan.Zero, true, connections.Count);
    }

    protected abstract void OnConnect(StratumConnection connection, IPEndPoint portItem1);

    protected async Task OnRequestAsync(StratumConnection connection, JsonRpcRequest request, CancellationToken ct)
    {
        // boot pre-connected clients
        if(banManager?.IsBanned(connection.RemoteEndpoint.Address) == true)
        {
            logger.Info(() => $"[{connection.ConnectionId}] Disconnecting banned client @ {connection.RemoteEndpoint.Address}");
            Disconnect(connection);
            return;
        }

        logger.Debug(() => $"[{connection.ConnectionId}] Dispatching request '{request.Method}' [{request.Id}]");

        var tsRequest = new Timestamped<JsonRpcRequest>(request, clock.Now);

        await OnRequestAsync(connection, tsRequest, ct);

        PublishTelemetry(TelemetryCategory.StratumRequest, request.Method, clock.Now - tsRequest.Timestamp);
    }

    protected void OnConnectionError(StratumConnection connection, Exception ex)
    {
        if(ex is AggregateException)
            ex = ex.InnerException;

        if(ex is IOException && ex.InnerException != null)
            ex = ex.InnerException;

        switch(ex)
        {
            case SocketException sockEx:
                if(!ignoredSocketErrors.Contains(sockEx.ErrorCode))
                    logger.Error(() => $"[{connection.ConnectionId}] Connection error: {ex}");
                break;

            case InvalidDataException idEx:
                logger.Error(() => $"[{connection.ConnectionId}] Connection error: {idEx}");
                break;

            case JsonException jsonEx:
                // junk received (invalid json)
                logger.Error(() => $"[{connection.ConnectionId}] Connection json error: {jsonEx.Message}");

                if(clusterConfig.Banning?.BanOnJunkReceive.HasValue == false || clusterConfig.Banning?.BanOnJunkReceive == true)
                {
                    logger.Info(() => $"[{connection.ConnectionId}] Banning client for sending junk");
                    banManager?.Ban(connection.RemoteEndpoint.Address, TimeSpan.FromMinutes(3));
                }
                break;

            case AuthenticationException authEx:
                // junk received (SSL handshake)
                logger.Error(() => $"[{connection.ConnectionId}] Connection json error: {authEx.Message}");

                if(clusterConfig.Banning?.BanOnJunkReceive.HasValue == false || clusterConfig.Banning?.BanOnJunkReceive == true)
                {
                    logger.Info(() => $"[{connection.ConnectionId}] Banning client for failing SSL handshake");
                    banManager?.Ban(connection.RemoteEndpoint.Address, TimeSpan.FromMinutes(3));
                }
                break;

            case IOException ioEx:
                // junk received (SSL handshake)
                logger.Error(() => $"[{connection.ConnectionId}] Connection json error: {ioEx.Message}");

                if(ioEx.Source == "System.Net.Security")
                {
                    if(clusterConfig.Banning?.BanOnJunkReceive.HasValue == false || clusterConfig.Banning?.BanOnJunkReceive == true)
                    {
                        logger.Info(() => $"[{connection.ConnectionId}] Banning client for failing SSL handshake");
                        banManager?.Ban(connection.RemoteEndpoint.Address, TimeSpan.FromMinutes(3));
                    }
                }
                break;

            case ObjectDisposedException:
                // socket disposed
                break;

            case ArgumentException argEx:
                if(argEx.TargetSite != streamWriterCtor || argEx.ParamName != "stream")
                    logger.Error(() => $"[{connection.ConnectionId}] Connection error: {ex}");
                break;

            case InvalidOperationException:
                // The source completed without providing data to receive
                break;

            default:
                logger.Error(() => $"[{connection.ConnectionId}] Connection error: {ex}");
                break;
        }

        UnregisterConnection(connection);
    }

    protected void OnConnectionComplete(StratumConnection connection)
    {
        logger.Debug(() => $"[{connection.ConnectionId}] Received EOF");

        UnregisterConnection(connection);
    }

    protected void Disconnect(StratumConnection connection)
    {
        Contract.RequiresNonNull(connection);

        connection.Disconnect();
    }

    private X509Certificate2 GetTlsCert(StratumEndpoint port)
    {
        if(!port.PoolEndpoint.Tls)
            return null;

        if(!certs.TryGetValue(port.PoolEndpoint.TlsPfxFile, out var cert))
        {
            cert = Guard(()=> new X509Certificate2(port.PoolEndpoint.TlsPfxFile, port.PoolEndpoint.TlsPfxPassword), ex =>
            {
                logger.Info(() => $"Failed to load TLS certificate {port.PoolEndpoint.TlsPfxFile}: {ex.Message}");
                throw ex;
            });

            certs[port.PoolEndpoint.TlsPfxFile] = cert;
        }

        return cert;
    }

    private bool DisconnectIfBanned(Socket socket, IPEndPoint remoteEndpoint)
    {
        if(remoteEndpoint == null || banManager == null)
            return false;

        if (banManager.IsBanned(remoteEndpoint.Address))
        {
            logger.Debug(() => $"Disconnecting banned ip {remoteEndpoint.Address}");
            socket.Close();

            return true;
        }

        return false;
    }

    protected void PublishTelemetry(TelemetryCategory cat, TimeSpan elapsed, bool? success = null, int? total = null)
    {
        messageBus.SendTelemetry(poolConfig.Id, cat, elapsed, success, null, total);
    }

    protected void PublishTelemetry(TelemetryCategory cat, string info, TimeSpan elapsed, bool? success = null, int? total = null)
    {
        messageBus.SendTelemetry(poolConfig.Id, cat, info, elapsed, success, null, total);
    }

    protected abstract Task OnRequestAsync(StratumConnection connection, Timestamped<JsonRpcRequest> request, CancellationToken ct);
}
