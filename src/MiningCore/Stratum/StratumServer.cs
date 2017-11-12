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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using MiningCore.Banning;
using MiningCore.Buffers;
using MiningCore.JsonRpc;
using MiningCore.Time;
using MiningCore.Util;
using NetUV.Core.Handles;
using NetUV.Core.Native;
using Newtonsoft.Json;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Stratum
{
    public abstract class StratumServer<TClientContext>
    {
        protected StratumServer(IComponentContext ctx, IMasterClock clock)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(clock, nameof(clock));

            this.ctx = ctx;
            this.clock = clock;
        }

        protected readonly Dictionary<string, StratumClient<TClientContext>> clients =
            new Dictionary<string, StratumClient<TClientContext>>();

        protected readonly IComponentContext ctx;
        protected readonly IMasterClock clock;
        protected readonly Dictionary<int, Tcp> ports = new Dictionary<int, Tcp>();
        protected IBanManager banManager;
        protected bool disableConnectionLogging = false;
        protected ILogger logger;

        protected abstract string LogCat { get; }

        public void StartListeners(params IPEndPoint[] stratumPorts)
        {
            Contract.RequiresNonNull(stratumPorts, nameof(stratumPorts));

            // start ports
            foreach(var endpoint in stratumPorts)
            {
                // host it and its message loop in a dedicated background thread
                var task = new Task(() =>
                {
                    var loop = new Loop();

                    var listener = loop
                        .CreateTcp()
                        .SimultaneousAccepts(true)
                        .KeepAlive(true, 1)
                        .NoDelay(true)
                        .Listen(endpoint, (con, ex) =>
                        {
                            if (ex == null)
                                OnClientConnected(con, endpoint, loop);
                            else
                                logger.Error(() => $"[{LogCat}] Connection error state: {ex.Message}");
                        });

                    lock(ports)
                    {
                        ports[endpoint.Port] = listener;
                    }

                    loop.RunDefault();
                }, TaskCreationOptions.LongRunning);

                task.Start();

                logger.Info(() => $"[{LogCat}] Stratum port {endpoint.Address}:{endpoint.Port} online");
            }
        }

        public void StopListeners()
        {
            lock(ports)
            {
                var portValues = ports.Values.ToArray();

                for(int i = 0; i < portValues.Length; i++)
                {
                    var listener = portValues[i];

                    listener.Shutdown((tcp, ex) =>
                    {
                        if (tcp?.IsValid == true)
                            tcp.Dispose();
                    });
                }
            }
        }

        private void OnClientConnected(Tcp con, IPEndPoint endpointConfig, Loop loop)
        {
            try
            {
                var remoteEndPoint = con.GetPeerEndPoint();

                // get rid of banned clients as early as possible
                if (banManager?.IsBanned(remoteEndPoint.Address) == true)
                {
                    logger.Trace(() => $"[{LogCat}] Disconnecting banned ip {remoteEndPoint.Address}");
                    con.Dispose();
                    return;
                }

                var connectionId = CorrelationIdGenerator.GetNextId();
                logger.Trace(() => $"[{LogCat}] Accepting connection [{connectionId}] from {remoteEndPoint.Address}:{remoteEndPoint.Port}");

                // setup client
                var client = new StratumClient<TClientContext>();
                client.Init(loop, con, ctx, endpointConfig, connectionId);

                // request subscription
                var sub = client.Received
                    .Select(data => Observable.FromAsync(() => Task.Run(() => // get off of LibUV event-loop-thread immediately
                    {
                        // boot pre-connected clients
                        if (banManager?.IsBanned(client.RemoteEndpoint.Address) == true)
                        {
                            logger.Trace(() => $"[{LogCat}] [{connectionId}] Disconnecting banned client @ {remoteEndPoint.Address}");
                            DisconnectClient(client);
                            data.Dispose();
                            return Unit.Default;
                        }

                        // parse request
                        var request = ParseRequest(data);

                        logger.Trace(() => $"[{LogCat}] [{client.ConnectionId}] Dispatching request {request.Method} [{request.Id}]");

                        // dispatch request
                        OnRequestAsync(client, new Timestamped<JsonRpcRequest>(request, clock.UtcNow)).Wait();

                        return Unit.Default;
                    })))
                    .Concat()
                    .Subscribe(_ => { }, ex => OnReceiveError(client, ex), () => OnReceiveComplete(client));

                // ensure subscription is disposed on loop thread
                var disposer = loop.CreateAsync((handle) =>
                {
                    sub.Dispose();

                    handle.Dispose();
                });

                client.Subscription = Disposable.Create(() => { disposer.Send(); });

                // register client
                lock(clients)
                {
                    clients[connectionId] = client;
                }

                OnConnect(client);
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => nameof(OnClientConnected));
            }
        }

        private JsonRpcRequest ParseRequest(PooledArraySegment<byte> data)
        {
            using (data)
            {
                using (var stream = new MemoryStream(data.Array, 0, data.Size))
                {
                    using (var reader = new StreamReader(stream, StratumClient<TClientContext>.Encoding))
                    {
                        using (var jreader = new JsonTextReader(reader))
                        {
                            return StratumClient<TClientContext>.Serializer.Deserialize<JsonRpcRequest>(jreader);
                        }
                    }
                }
            }
        }

        protected virtual void OnReceiveError(StratumClient<TClientContext> client, Exception ex)
        {
            switch(ex)
            {
                case OperationException opEx:
                    // log everything but ECONNRESET which just indicates the client disconnecting
                    if (opEx.ErrorCode != ErrorCode.ECONNRESET)
                        logger.Error(() => $"[{LogCat}] [{client.ConnectionId}] Connection error state: {ex.Message}");
                    break;

                case JsonReaderException jsonEx:
                    // ban clients sending junk
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Banning client for sending junk");
                    banManager.Ban(client.RemoteEndpoint.Address, TimeSpan.FromMinutes(30));
                    break;
            }

            DisconnectClient(client);
        }

        protected virtual void OnReceiveComplete(StratumClient<TClientContext> client)
        {
            logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Received EOF");

            DisconnectClient(client);
        }

        protected virtual void DisconnectClient(StratumClient<TClientContext> client)
        {
            Contract.RequiresNonNull(client, nameof(client));

            var subscriptionId = client.ConnectionId;

            client.Disconnect();

            if (!string.IsNullOrEmpty(subscriptionId))
            {
                // unregister client
                lock(clients)
                {
                    clients.Remove(subscriptionId);
                }
            }

            OnDisconnect(subscriptionId);
        }

        protected void ForEachClient(Action<StratumClient<TClientContext>> action)
        {
            StratumClient<TClientContext>[] tmp;

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

        protected abstract void OnConnect(StratumClient<TClientContext> client);
        protected abstract void OnDisconnect(string subscriptionId);

        protected abstract Task OnRequestAsync(StratumClient<TClientContext> client,
            Timestamped<JsonRpcRequest> request);
    }
}
