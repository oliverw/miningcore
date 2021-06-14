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
                    32,  // EPIPE
                };
            }
        }

        protected readonly Dictionary<string, StratumClient> clients = new();
        protected static readonly ConcurrentDictionary<string, X509Certificate2> certs = new();
        protected static readonly HashSet<int> ignoredSocketErrors;
        protected static readonly MethodBase StreamWriterCtor = typeof(StreamWriter).GetConstructor(new[] { typeof(Stream), typeof(Encoding), typeof(int), typeof(bool) });

        protected readonly IComponentContext ctx;
        protected readonly IMasterClock clock;
        protected readonly Dictionary<int, Socket> ports = new();
        protected ClusterConfig clusterConfig;
        protected IBanManager banManager;
        protected ILogger logger;

        public void StartListeners(params (IPEndPoint IPEndPoint, PoolEndpoint PoolEndpoint)[] stratumPorts)
        {
            Contract.RequiresNonNull(stratumPorts, nameof(stratumPorts));

            Task.Run(async () =>
            {
                // Setup sockets
                var sockets = stratumPorts.Select(port =>
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

                logger.Info(() => $"Stratum ports {string.Join(", ", stratumPorts.Select(x => $"{x.IPEndPoint.Address}:{x.IPEndPoint.Port}").ToArray())} online");

                // Setup accept tasks
                var tasks = sockets.Select(socket => socket.AcceptAsync()).ToArray();

                while(true)
                {
                    try
                    {
                        // Wait incoming connection on any of the monitored sockets
                        await Task.WhenAny(tasks);

                        // check tasks
                        for(var i = 0; i < tasks.Length; i++)
                        {
                            var task = tasks[i];
                            var port = stratumPorts[i];

                            // skip running tasks
                            if(!(task.IsCompleted || task.IsFaulted || task.IsCanceled))
                                continue;

                            // accept connection if successful
                            if(task.IsCompletedSuccessfully)
                                AcceptConnection(task.Result, port);

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
            });
        }

        private void AcceptConnection(Socket socket, (IPEndPoint IPEndPoint, PoolEndpoint PoolEndpoint) port)
        {
            var remoteEndpoint = (IPEndPoint) socket.RemoteEndPoint;
            var connectionId = CorrelationIdGenerator.GetNextId();

            // get rid of banned clients as early as possible
            if(banManager?.IsBanned(remoteEndpoint.Address) == true)
            {
                logger.Debug(() => $"Disconnecting banned ip {remoteEndpoint.Address}");
                socket.Close();
                return;
            }

            // TLS cert loading
            X509Certificate2 cert = null;

            if(port.PoolEndpoint.Tls)
            {
                if(!certs.TryGetValue(port.PoolEndpoint.TlsPfxFile, out cert))
                    cert = AddCert(port);
            }

            // setup client
            var client = new StratumClient(logger, clock, connectionId);

            RegisterClient(client, connectionId);
            OnConnect(client, port.IPEndPoint);
            client.Run(socket, port, cert, OnRequestAsync, OnClientComplete, OnClientError);
        }

        public void StopListeners()
        {
            lock(ports)
            {
                var portValues = ports.Values.ToArray();

                for(int i = 0; i < portValues.Length; i++)
                {
                    var socket = portValues[i];

                    socket.Close();
                }
            }
        }

        protected virtual void RegisterClient(StratumClient client, string connectionId)
        {
            Contract.RequiresNonNull(client, nameof(client));

            lock(clients)
            {
                clients[connectionId] = client;
            }
        }

        protected virtual void UnregisterClient(StratumClient client)
        {
            Contract.RequiresNonNull(client, nameof(client));

            var subscriptionId = client.ConnectionId;

            if(!string.IsNullOrEmpty(subscriptionId))
            {
                lock(clients)
                {
                    clients.Remove(subscriptionId);
                }
            }
        }

        protected abstract void OnConnect(StratumClient client, IPEndPoint portItem1);

        protected async Task OnRequestAsync(StratumClient client, JsonRpcRequest request, CancellationToken ct)
        {
            // boot pre-connected clients
            if(banManager?.IsBanned(client.RemoteEndpoint.Address) == true)
            {
                logger.Info(() => $"[{client.ConnectionId}] Disconnecting banned client @ {client.RemoteEndpoint.Address}");
                DisconnectClient(client);
                return;
            }

            logger.Debug(() => $"[{client.ConnectionId}] Dispatching request '{request.Method}' [{request.Id}]");

            await OnRequestAsync(client, new Timestamped<JsonRpcRequest>(request, clock.Now), ct);
        }

        protected virtual void OnClientError(StratumClient client, Exception ex)
        {
            if(ex is AggregateException)
                ex = ex.InnerException;

            if(ex is IOException && ex.InnerException != null)
                ex = ex.InnerException;

            switch(ex)
            {
                case SocketException sockEx:
                    if(!ignoredSocketErrors.Contains(sockEx.ErrorCode))
                        logger.Error(() => $"[{client.ConnectionId}] Connection error state: {ex}");
                    break;

                case JsonException jsonEx:
                    // junk received (invalid json)
                    logger.Error(() => $"[{client.ConnectionId}] Connection json error state: {jsonEx.Message}");

                    if(clusterConfig.Banning?.BanOnJunkReceive.HasValue == false || clusterConfig.Banning?.BanOnJunkReceive == true)
                    {
                        logger.Info(() => $"[{client.ConnectionId}] Banning client for sending junk");
                        banManager?.Ban(client.RemoteEndpoint.Address, TimeSpan.FromMinutes(3));
                    }
                    break;

                case AuthenticationException authEx:
                    // junk received (SSL handshake)
                    logger.Error(() => $"[{client.ConnectionId}] Connection json error state: {authEx.Message}");

                    if(clusterConfig.Banning?.BanOnJunkReceive.HasValue == false || clusterConfig.Banning?.BanOnJunkReceive == true)
                    {
                        logger.Info(() => $"[{client.ConnectionId}] Banning client for failing SSL handshake");
                        banManager?.Ban(client.RemoteEndpoint.Address, TimeSpan.FromMinutes(3));
                    }
                    break;

                case IOException ioEx:
                    // junk received (SSL handshake)
                    logger.Error(() => $"[{client.ConnectionId}] Connection json error state: {ioEx.Message}");

                    if(ioEx.Source == "System.Net.Security")
                    {
                        if(clusterConfig.Banning?.BanOnJunkReceive.HasValue == false || clusterConfig.Banning?.BanOnJunkReceive == true)
                        {
                            logger.Info(() => $"[{client.ConnectionId}] Banning client for failing SSL handshake");
                            banManager?.Ban(client.RemoteEndpoint.Address, TimeSpan.FromMinutes(3));
                        }
                    }
                    break;

                case ObjectDisposedException odEx:
                    // socket disposed
                    break;

                case ArgumentException argEx:
                    if(argEx.TargetSite != StreamWriterCtor || argEx.ParamName != "stream")
                        logger.Error(() => $"[{client.ConnectionId}] Connection error state: {ex}");
                    break;

                case InvalidOperationException invOpEx:
                    // The source completed without providing data to receive
                    break;

                default:
                    logger.Error(() => $"[{client.ConnectionId}] Connection error state: {ex}");
                    break;
            }

            UnregisterClient(client);
        }

        protected virtual void OnClientComplete(StratumClient client)
        {
            logger.Debug(() => $"[{client.ConnectionId}] Received EOF");

            UnregisterClient(client);
        }

        protected virtual void DisconnectClient(StratumClient client)
        {
            Contract.RequiresNonNull(client, nameof(client));

            client.Disconnect();
            UnregisterClient(client);
        }

        private X509Certificate2 AddCert((IPEndPoint IPEndPoint, PoolEndpoint PoolEndpoint) port)
        {
            try
            {
                var tlsCert = new X509Certificate2(port.PoolEndpoint.TlsPfxFile);
                certs.TryAdd(port.PoolEndpoint.TlsPfxFile, tlsCert);
                return tlsCert;
            }

            catch(Exception ex)
            {
                logger.Info(() => $"Failed to load TLS certificate {port.PoolEndpoint.TlsPfxFile}: {ex.Message}");
                throw;
            }
        }

        protected void ForEachClient(Action<StratumClient> action)
        {
            StratumClient[] tmp;

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

        protected IEnumerable<Task> ForEachClient(Func<StratumClient, Task> func)
        {
            StratumClient[] tmp;

            lock(clients)
            {
                tmp = clients.Values.ToArray();
            }

            return tmp.Select(x => func(x));
        }

        protected abstract Task OnRequestAsync(StratumClient client,
            Timestamped<JsonRpcRequest> request, CancellationToken ct);
    }
}
