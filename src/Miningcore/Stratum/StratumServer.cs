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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
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
                    32, // EPIPE
                };
            }
        }

        protected record ConnectionContext(StratumConnection Connection, Task Task);
        protected readonly Dictionary<string, ConnectionContext> connections = new();
        protected Task connectionsTask;
        protected static readonly ConcurrentDictionary<string, X509Certificate2> certs = new();
        protected static readonly HashSet<int> ignoredSocketErrors;
        protected static readonly MethodBase StreamWriterCtor = typeof(StreamWriter).GetConstructor(new[] {typeof(Stream), typeof(Encoding), typeof(int), typeof(bool)});

        protected readonly IComponentContext ctx;
        protected readonly IMasterClock clock;
        protected readonly Dictionary<int, Socket> ports = new();
        protected ClusterConfig clusterConfig;
        protected IBanManager banManager;
        protected ILogger logger;

        public async Task RunAsync(CancellationToken ct, params StratumEndpoint[] endpoints)
        {
            Contract.RequiresNonNull(endpoints, nameof(endpoints));

            // Setup sockets
            var sockets = endpoints.Select(port =>
            {
                // Setup socket
                var server = new Socket(SocketType.Stream, ProtocolType.Tcp);
                server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                server.Bind(port.IpEndPoint);
                server.Listen(512);

                lock(ports)
                {
                    ports[port.IpEndPoint.Port] = server;
                }

                return server;
            }).ToArray();

            logger.Info(() => $"Stratum ports {string.Join(", ", endpoints.Select(x => $"{x.IpEndPoint.Address}:{x.IpEndPoint.Port}").ToArray())} online");

            // Setup accept tasks
            var acceptTasks = sockets.Select(socket => socket.AcceptAsync()).ToArray();

            while(!ct.IsCancellationRequested)
            {
                try
                {
                    // build compound task
                    Task task;

                    lock(connections)
                    {
                        if(connectionsTask != null)
                            task = Task.WhenAny(Task.WhenAny(acceptTasks), connectionsTask);
                        else
                            task = Task.WhenAny(acceptTasks);
                    }

                    // wait for either a client connect to a socket or an existing connection to terminate
                    await task;

                    // check socket tasks for changes
                    for(var i = 0; i < acceptTasks.Length; i++)
                    {
                        var acceptTask = acceptTasks[i];
                        var port = endpoints[i];

                        // skip running tasks
                        if(!(acceptTask.IsCompleted || acceptTask.IsFaulted || acceptTask.IsCanceled))
                            continue;

                        if(acceptTask.IsCompletedSuccessfully && acceptTask.Result != null)
                        {
                            var connection = AcceptConnection(acceptTask.Result, port, ct);

                            if(connection != null)
                            {
                                OnConnect(connection, port.IpEndPoint);

                                RegisterConnection(connection,
                                    connection.RunAsync(ct, port, OnRequestAsync, OnConnectionComplete, OnConnectionError));
                            }
                        }

                        // Refresh task
                        acceptTasks[i] = sockets[i].AcceptAsync();
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

        private StratumConnection AcceptConnection(Socket socket, StratumEndpoint port, CancellationToken ct)
        {
            var remoteEndpoint = (IPEndPoint) socket.RemoteEndPoint;

            // dispose of banned clients as early as possible
            if(remoteEndpoint != null && banManager?.IsBanned(remoteEndpoint.Address) == true)
            {
                logger.Debug(() => $"Disconnecting banned ip {remoteEndpoint.Address}");
                socket.Close();
                return null;
            }

            // unique connection id
            var connectionId = CorrelationIdGenerator.GetNextId();

            // TLS cert loading
            X509Certificate2 cert = null;

            if(port.PoolEndpoint.Tls)
            {
                if(!certs.TryGetValue(port.PoolEndpoint.TlsPfxFile, out cert))
                    cert = AddCert(port);
            }

            // create it but don't run it yet
            return new StratumConnection(logger, clock, connectionId, socket, cert);
        }

        protected virtual void RegisterConnection(StratumConnection connection, Task task)
        {
            Contract.RequiresNonNull(connection, nameof(connection));
            Contract.RequiresNonNull(task, nameof(task));

            logger.Debug(() => $"Registering connection {connection.ConnectionId}");

            lock(connections)
            {
                connections[connection.ConnectionId] = new ConnectionContext(connection, task);

                BuildConnectionsTask();
            }
        }

        protected virtual void UnregisterConnection(StratumConnection connection)
        {
            Contract.RequiresNonNull(connection, nameof(connection));

            logger.Debug(() => $"Unregistering connection {connection.ConnectionId}");

            lock(connections)
            {
                connections.Remove(connection.ConnectionId);

                BuildConnectionsTask();
            }
        }

        protected void BuildConnectionsTask()
        {
            var tasks = connections.Values;

            logger.Debug(() => "Building connection task");

            connectionsTask = tasks.Any() ?
                Task.WhenAny(connections.Values.Select(x => x.Task)) :
                null;
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
                certs.TryAdd(endpoint.PoolEndpoint.TlsPfxFile, tlsCert);
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
            ConnectionContext[] tmp;

            lock(connections)
            {
                tmp = connections.Values.ToArray();
            }

            foreach(var (connection, _) in tmp)
            {
                try
                {
                    action(connection);
                }

                catch(Exception ex)
                {
                    logger.Error(ex);
                }
            }
        }

        protected IEnumerable<Task> ForEachConnection(Func<StratumConnection, Task> func)
        {
            ConnectionContext[] tmp;

            lock(connections)
            {
                tmp = connections.Values.ToArray();
            }

            return tmp.Select(x => func(x.Connection));
        }

        protected abstract Task OnRequestAsync(StratumConnection connection,
            Timestamped<JsonRpcRequest> request, CancellationToken ct);
    }
}
