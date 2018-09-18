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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.EntityFrameworkCore.Internal;
using MiningCore.Banning;
using MiningCore.Buffers;
using MiningCore.Configuration;
using MiningCore.JsonRpc;
using MiningCore.Time;
using MiningCore.Util;
using Newtonsoft.Json;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Stratum
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

        protected readonly Dictionary<string, StratumClient> clients = new Dictionary<string, StratumClient>();

        protected readonly IComponentContext ctx;
        protected readonly IMasterClock clock;
        protected readonly Dictionary<int, Socket> ports = new Dictionary<int, Socket>();
        protected ClusterConfig clusterConfig;
        protected IBanManager banManager;
        protected ILogger logger;

        protected abstract string LogCat { get; }

        public void StartListeners(params (IPEndPoint IPEndPoint, TcpProxyProtocolConfig ProxyProtocol)[] stratumPorts)
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

                    lock (ports)
                    {
                        ports[port.IPEndPoint.Port] = server;
                    }

                    logger.Info(() => $"[{LogCat}] Stratum port {port.IPEndPoint.Address}:{port.IPEndPoint.Port} online");

                    return server;
                }).ToArray();

                // Setup accept tasks
                var tasks = sockets.Select(s => s.AcceptAsync()).ToArray();

                while (true)
                {
                    try
                    {
                        // Wait incoming connection on any of the monitored sockets
                        await Task.WhenAny(tasks);

                        // check tasks
                        for (var i = 0; i < tasks.Length; i++)
                        {
                            var task = tasks[i];
                            var port = stratumPorts[i];

                            // skip running tasks
                            if (!(task.IsCompleted || task.IsFaulted || task.IsCanceled))
                                continue;

                            // accept connection if successful
                            if (task.IsCompletedSuccessfully)
                                AcceptConnection(task.Result, port);

                            // Create accept task replacement
                            tasks[i] = sockets[i].AcceptAsync();
                        }
                    }

                    catch (Exception ex)
                    {
                        logger.Error(ex);
                    }
                }
            });
        }

        private void AcceptConnection(Socket socket, (IPEndPoint IPEndPoint, TcpProxyProtocolConfig ProxyProtocol) port)
        {
            var remoteEndpoint = (IPEndPoint) socket.RemoteEndPoint;
            var connectionId = CorrelationIdGenerator.GetNextId();

            logger.Debug(() => $"[{LogCat}] Accepting connection [{connectionId}] from {remoteEndpoint.Address}:{remoteEndpoint.Port}");

            // get rid of banned clients as early as possible
            if (banManager?.IsBanned(remoteEndpoint.Address) == true)
            {
                logger.Debug(() => $"[{LogCat}] Disconnecting banned ip {remoteEndpoint.Address}");
                socket.Close();
                return;
            }

            // prepare socket
            socket.NoDelay = true;

            // setup client
            var client = new StratumClient(socket, clock, port.IPEndPoint, connectionId);

            lock (clients)
            {
                clients[connectionId] = client;
            }

            OnConnect(client);

            client.Run(port, OnRequestAsync, OnReceiveComplete, OnReceiveError);
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

        protected abstract void OnConnect(StratumClient client);

        protected async Task OnRequestAsync(StratumClient client, JsonRpcRequest request)
        {
            // boot pre-connected clients
            if (banManager?.IsBanned(client.RemoteEndpoint.Address) == true)
            {
                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Disconnecting banned client @ {client.RemoteEndpoint.Address}");
                DisconnectClient(client);
                return;
            }

            try
            {
                logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Dispatching request '{request.Method}' [{request.Id}]");

                await OnRequestAsync(client, new Timestamped<JsonRpcRequest>(request, clock.Now));
            }

            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ": " + ex : "";

                if (request != null)
                    logger.Error(ex, () => $"[{LogCat}] [{client.ConnectionId}] Error processing request {request.Method} [{request.Id}]{innerEx}");
                else
                    logger.Error(ex, () => $"[{LogCat}] [{client.ConnectionId}] Error processing request{innerEx}");

                throw;
            }
        }

        protected virtual void OnReceiveError(StratumClient client, Exception ex)
        {
            switch (ex)
            {
                case SocketException sockEx:
                    // log everything but ECONNRESET which just indicates the client disconnecting
                    if (sockEx.ErrorCode != (int) SocketError.ConnectionReset &&
                        sockEx.ErrorCode != (int) SocketError.ConnectionAborted &&
                        sockEx.ErrorCode != (int) SocketError.OperationAborted)
                    {
                        logger.Error(() => $"[{LogCat}] [{client.ConnectionId}] Connection error state: {ex}");
                    }
                    break;

                case JsonException jsonEx:
                    // junk received (no valid json)
                    logger.Error(() => $"[{LogCat}] [{client.ConnectionId}] Connection json error state: {jsonEx.Message}");

                    if (clusterConfig.Banning?.BanOnJunkReceive.HasValue == false || clusterConfig.Banning?.BanOnJunkReceive == true)
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Banning client for sending junk");
                        banManager?.Ban(client.RemoteEndpoint.Address, TimeSpan.FromMinutes(30));
                    }
                    break;

                default:
                    logger.Error(() => $"[{LogCat}] [{client.ConnectionId}] Connection error state: {ex}");
                    break;
            }

            DisconnectClient(client);
        }

        protected virtual void OnReceiveComplete(StratumClient client)
        {
            logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Received EOF");

            DisconnectClient(client);
        }

        protected virtual void DisconnectClient(StratumClient client)
        {
            Contract.RequiresNonNull(client, nameof(client));

            var subscriptionId = client.ConnectionId;

            client.Disconnect();

            if (!string.IsNullOrEmpty(subscriptionId))
            {
                // unregister client
                lock (clients)
                {
                    clients.Remove(subscriptionId);
                }
            }

            OnDisconnect(subscriptionId);
        }

        protected void ForEachClient(Action<StratumClient> action)
        {
            StratumClient[] tmp;

            lock (clients)
            {
                tmp = clients.Values.ToArray();
            }

            foreach (var client in tmp)
            {
                try
                {
                    action(client);
                }

                catch (Exception ex)
                {
                    logger.Error(ex);
                }
            }
        }

        protected IEnumerable<Task> ForEachClient(Func<StratumClient, Task> func)
        {
            StratumClient[] tmp;

            lock (clients)
            {
                tmp = clients.Values.ToArray();
            }

            return tmp.Select(x => func(x));
        }

        protected virtual void OnDisconnect(string subscriptionId)
        {
        }

        protected abstract Task OnRequestAsync(StratumClient client,
            Timestamped<JsonRpcRequest> request);
    }
}
