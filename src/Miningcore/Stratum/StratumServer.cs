using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Miningcore.JsonRpc;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using NLog;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Stratum
{
    public abstract class StratumServer
    {
        protected StratumServer(IComponentContext ctx, IMasterClock clock)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(clock, nameof(clock));

            this.ctx = ctx;
            this.clock = clock;
        }

        static StratumServer()
        {
            if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                IgnoredSocketErrors = new HashSet<int>
                {
                    (int) SocketError.ConnectionReset,
                    (int) SocketError.ConnectionAborted,
                    (int) SocketError.OperationAborted
                };
            }

            else if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // see: http://www.virtsync.com/c-error-codes-include-errno
                IgnoredSocketErrors = new HashSet<int>
                {
                    104, // ECONNRESET
                    125, // ECANCELED
                    103, // ECONNABORTED
                    110, // ETIMEDOUT
                    32,  // EPIPE
                };
            }
        }

        protected readonly Dictionary<string, StratumConnection> clients = new();
        protected static readonly ConcurrentDictionary<string, X509Certificate2> Certs = new();
        protected static readonly HashSet<int> IgnoredSocketErrors;
        protected static readonly MethodBase StreamWriterCtor = typeof(StreamWriter).GetConstructor(new[] { typeof(Stream), typeof(Encoding), typeof(int), typeof(bool) });

        protected readonly IComponentContext ctx;
        protected readonly IMasterClock clock;
        protected readonly Dictionary<int, Socket> ports = new();
        protected ClusterConfig clusterConfig;
        protected IBanManager banManager;
        protected ILogger logger;

        public async Task ServeStratum(CancellationToken ct, params StratumEndpoint[] endpoints)
        {
            Contract.RequiresNonNull(endpoints, nameof(endpoints));

            // Setup sockets
            var sockets = endpoints.Select(port =>
            {
                // Setup socket
                var server = new Socket(SocketType.Stream, ProtocolType.Tcp);
                server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                server.Bind(port.IPEndPoint);
                server.Listen(512);

                lock(ports)
                {
                    ports[port.IPEndPoint.Port] = server;
                }

                return server;
            }).ToArray();

            logger.Info(() => $"Stratum ports {string.Join(", ", endpoints.Select(x => $"{x.IPEndPoint.Address}:{x.IPEndPoint.Port}").ToArray())} online");

            // Setup accept tasks
            var tasks = sockets.Select(socket => socket.AcceptAsync()).ToArray();

            while(!ct.IsCancellationRequested)
            {
                try
                {
                    // Wait incoming connection
                    await Task.WhenAny(tasks);

                    // check tasks
                    for(var i = 0; i < tasks.Length; i++)
                    {
                        var task = tasks[i];
                        var port = endpoints[i];

                        // skip running tasks
                        if(!(task.IsCompleted || task.IsFaulted || task.IsCanceled))
                            continue;

                        // accept connection if successful
                        if(task.IsCompletedSuccessfully)
                            AcceptConnection(task.Result, port, ct);

                        // Refresh task
                        tasks[i] = sockets[i].AcceptAsync();
                    }
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

        private void AcceptConnection(Socket socket, StratumEndpoint port, CancellationToken ct)
        {
            var remoteEndpoint = (IPEndPoint) socket.RemoteEndPoint;

            // dispose of banned clients as early as possible
            if(remoteEndpoint != null && banManager?.IsBanned(remoteEndpoint.Address) == true)
            {
                logger.Debug(() => $"Disconnecting banned ip {remoteEndpoint.Address}");
                socket.Close();
                return;
            }

            var connectionId = CorrelationIdGenerator.GetNextId();

            // TLS cert loading
            X509Certificate2 cert = null;

            if(port.PoolEndpoint.Tls)
            {
                if(!Certs.TryGetValue(port.PoolEndpoint.TlsPfxFile, out cert))
                    cert = AddCert(port);
            }

            // setup client
            var connection = new StratumConnection(logger, clock, connectionId);

            RegisterConnection(connection, connectionId);
            OnConnect(connection, port.IPEndPoint);
            connection.DispatchAsync(socket, ct, port, cert, OnRequestAsync, OnConnectionComplete, OnConnectionError);
        }

        protected virtual void RegisterConnection(StratumConnection connection, string connectionId)
        {
            Contract.RequiresNonNull(connection, nameof(connection));

            lock(clients)
            {
                clients[connectionId] = connection;
            }
        }

        protected virtual void UnregisterConnection(StratumConnection connection)
        {
            Contract.RequiresNonNull(connection, nameof(connection));

            var subscriptionId = connection.ConnectionId;

            if(!string.IsNullOrEmpty(subscriptionId))
            {
                lock(clients)
                {
                    clients.Remove(subscriptionId);
                }
            }
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
                    if(!IgnoredSocketErrors.Contains(sockEx.ErrorCode))
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
                    if(argEx.TargetSite != StreamWriterCtor || argEx.ParamName != "stream")
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

        private X509Certificate2 AddCert(StratumEndpoint endpoint)
        {
            try
            {
                var tlsCert = new X509Certificate2(endpoint.PoolEndpoint.TlsPfxFile);
                Certs.TryAdd(endpoint.PoolEndpoint.TlsPfxFile, tlsCert);
                return tlsCert;
            }

            catch(Exception ex)
            {
                logger.Info(() => $"Failed to load TLS certificate {endpoint.PoolEndpoint.TlsPfxFile}: {ex.Message}");
                throw;
            }
        }

        protected void ForEachConnection(Action<StratumConnection> action)
        {
            StratumConnection[] tmp;

            lock(clients)
            {
                tmp = clients.Values.ToArray();
            }

            foreach(var client in tmp)
            {
                try
                {
                    action(client);
                }

                catch(Exception ex)
                {
                    logger.Error(ex);
                }
            }
        }

        protected IEnumerable<Task> ForEachConnection(Func<StratumConnection, Task> func)
        {
            StratumConnection[] tmp;

            lock(clients)
            {
                tmp = clients.Values.ToArray();
            }

            return tmp.Select(func);
        }

        protected abstract Task OnRequestAsync(StratumConnection connection, Timestamped<JsonRpcRequest> request, CancellationToken ct);
    }
}
