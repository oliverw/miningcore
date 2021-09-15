using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
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

namespace Miningcore.Stratum
{
    public abstract class StratumServer
    {
        protected StratumServer(
            IComponentContext ctx,
            IMessageBus messageBus,
            IMasterClock clock)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));
            Contract.RequiresNonNull(clock, nameof(clock));

            this.ctx = ctx;
            this.messageBus = messageBus;
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
        protected readonly IMasterClock clock;
        protected ClusterConfig clusterConfig;
        protected PoolConfig poolConfig;
        protected IBanManager banManager;
        protected ILogger logger;

        public Task RunAsync(CancellationToken ct, params StratumEndpoint[] endpoints)
        {
            Contract.RequiresNonNull(endpoints, nameof(endpoints));

            logger.Info(() => $"Stratum ports {string.Join(", ", endpoints.Select(x => $"{x.IPEndPoint.Address}:{x.IPEndPoint.Port}").ToArray())} online");

            var tasks = endpoints.Select(port =>
            {
                var server = new Socket(SocketType.Stream, ProtocolType.Tcp);
                server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                server.Bind(port.IPEndPoint);
                server.Listen();

                return Task.Run(()=> Listen(server, port, ct), ct);
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
                    var socket = await server.AcceptAsync();

                    AcceptConnection(socket, port, cert, ct);
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

                // dispose of banned clients as early as possible
                if (DisconnectIfBanned(socket, remoteEndpoint))
                    return;

                // init connection
                var connection = new StratumConnection(logger, clock, CorrelationIdGenerator.GetNextId());

                logger.Info(() => $"[{connection.ConnectionId}] Accepting connection from {remoteEndpoint.Address}:{remoteEndpoint.Port} ...");

                RegisterConnection(connection);
                OnConnect(connection, port.IPEndPoint);

                connection.DispatchAsync(socket, ct, port, remoteEndpoint, cert, OnRequestAsync, OnConnectionComplete, OnConnectionError);
            }, ex=> logger.Error(ex)), ct);
        }

        protected virtual void RegisterConnection(StratumConnection connection)
        {
            var result = connections.TryAdd(connection.ConnectionId, connection);
            Debug.Assert(result);

            PublishTelemetry(TelemetryCategory.Connections, TimeSpan.Zero, true, connections.Count);
        }

        protected virtual void UnregisterConnection(StratumConnection connection)
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
                CloseConnection(connection);
                return;
            }

            logger.Debug(() => $"[{connection.ConnectionId}] Dispatching request '{request.Method}' [{request.Id}]");

            await OnRequestAsync(connection, new Timestamped<JsonRpcRequest>(request, clock.Now), ct);
        }

        protected virtual void OnConnectionError(StratumConnection connection, Exception ex)
        {
            if(ex is AggregateException)
                ex = ex.InnerException;

            if(ex is IOException && ex.InnerException != null)
                ex = ex.InnerException;

            switch(ex)
            {
                case SocketException sockEx:
                    if(!ignoredSocketErrors.Contains(sockEx.ErrorCode))
                        logger.Error(() => $"[{connection.ConnectionId}] Connection error state: {ex}");
                    break;

                case JsonException jsonEx:
                    // junk received (invalid json)
                    logger.Error(() => $"[{connection.ConnectionId}] Connection json error state: {jsonEx.Message}");

                    if(clusterConfig.Banning?.BanOnJunkReceive.HasValue == false || clusterConfig.Banning?.BanOnJunkReceive == true)
                    {
                        logger.Info(() => $"[{connection.ConnectionId}] Banning client for sending junk");
                        banManager?.Ban(connection.RemoteEndpoint.Address, TimeSpan.FromMinutes(3));
                    }
                    break;

                case AuthenticationException authEx:
                    // junk received (SSL handshake)
                    logger.Error(() => $"[{connection.ConnectionId}] Connection json error state: {authEx.Message}");

                    if(clusterConfig.Banning?.BanOnJunkReceive.HasValue == false || clusterConfig.Banning?.BanOnJunkReceive == true)
                    {
                        logger.Info(() => $"[{connection.ConnectionId}] Banning client for failing SSL handshake");
                        banManager?.Ban(connection.RemoteEndpoint.Address, TimeSpan.FromMinutes(3));
                    }
                    break;

                case IOException ioEx:
                    // junk received (SSL handshake)
                    logger.Error(() => $"[{connection.ConnectionId}] Connection json error state: {ioEx.Message}");

                    if(ioEx.Source == "System.Net.Security")
                    {
                        if(clusterConfig.Banning?.BanOnJunkReceive.HasValue == false || clusterConfig.Banning?.BanOnJunkReceive == true)
                        {
                            logger.Info(() => $"[{connection.ConnectionId}] Banning client for failing SSL handshake");
                            banManager?.Ban(connection.RemoteEndpoint.Address, TimeSpan.FromMinutes(3));
                        }
                    }
                    break;

                case ObjectDisposedException odEx:
                    // socket disposed
                    break;

                case ArgumentException argEx:
                    if(argEx.TargetSite != streamWriterCtor || argEx.ParamName != "stream")
                        logger.Error(() => $"[{connection.ConnectionId}] Connection error state: {ex}");
                    break;

                case InvalidOperationException invOpEx:
                    // The source completed without providing data to receive
                    break;

                default:
                    logger.Error(() => $"[{connection.ConnectionId}] Connection error state: {ex}");
                    break;
            }

            UnregisterConnection(connection);
        }

        protected virtual void OnConnectionComplete(StratumConnection connection)
        {
            logger.Debug(() => $"[{connection.ConnectionId}] Received EOF");

            UnregisterConnection(connection);
        }

        protected virtual void CloseConnection(StratumConnection connection)
        {
            Contract.RequiresNonNull(connection, nameof(connection));

            connection.Disconnect();

            UnregisterConnection(connection);
        }

        private X509Certificate2 GetTlsCert(StratumEndpoint port)
        {
            if(!port.PoolEndpoint.Tls)
                return null;

            if(!certs.TryGetValue(port.PoolEndpoint.TlsPfxFile, out var cert))
            {
                cert = Guard(()=> new X509Certificate2(port.PoolEndpoint.TlsPfxFile), ex =>
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

        protected IEnumerable<Task> ForEachConnection(Func<StratumConnection, Task> func)
        {
            var tmp = connections.Values.ToArray();

            return tmp.Select(func);
        }

        protected void PublishTelemetry(TelemetryCategory cat, TimeSpan elapsed, bool? success = null, int? total = null)
        {
            messageBus.SendTelemetry(poolConfig.Id, cat, elapsed, success, null, total);
        }

        protected abstract Task OnRequestAsync(StratumConnection connection, Timestamped<JsonRpcRequest> request, CancellationToken ct);
    }
}
