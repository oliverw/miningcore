using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Autofac;
using MiningCore.Banning;
using MiningCore.JsonRpc;
using MiningCore.Util;
using NetUV.Core.Handles;
using NetUV.Core.Native;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Stratum
{
    public abstract class StratumServer<TClientContext>
    {
        protected StratumServer(IComponentContext ctx)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));

            this.ctx = ctx;
        }

        protected readonly Dictionary<string, Tuple<StratumClient<TClientContext>, IDisposable>> clients =
            new Dictionary<string, Tuple<StratumClient<TClientContext>, IDisposable>>();

        protected readonly IComponentContext ctx;
        protected readonly Dictionary<int, IDisposable> ports = new Dictionary<int, IDisposable>();
        protected IBanManager banManager;
        protected bool disableConnectionLogging = false;
        protected ILogger logger;

        protected abstract string LogCat { get; }

        protected void StartListeners(IPEndPoint[] stratumPorts)
        {
            Contract.RequiresNonNull(ports, nameof(ports));

            // start ports
            foreach (var endpoint in stratumPorts)
            {
                // host it and its message loop in a dedicated background thread
                var task = new Task(() =>
                {
                    var loop = new Loop();

                    var listener = loop
                        .CreateTcp()
                        .SimultaneousAccepts(true)
                        .KeepAlive(false, 0)
                        .NoDelay(false)
                        .Listen(endpoint, (con, ex) =>
                        {
                            if (ex == null)
                                OnClientConnected(con, endpoint);
                            else
                                logger.Error(() => $"[{LogCat}] Connection error state: {ex.Message}");
                        });

                    lock (ports)
                    {
                        ports[endpoint.Port] = listener;
                    }

                    loop.RunDefault();
                }, TaskCreationOptions.LongRunning);

                task.Start();

                logger.Info(() => $"[{LogCat}] Stratum port {endpoint.Address}:{endpoint.Port} online");
            }
        }

        private void OnClientConnected(Tcp con, IPEndPoint endpointConfig)
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
                con.UserToken = connectionId;
                logger.Trace(() => $"[{LogCat}] Accepting connection [{connectionId}] from {remoteEndPoint.Address}:{remoteEndPoint.Port}");

                // setup client
                var client = new StratumClient<TClientContext>();
                client.Init(con, ctx, endpointConfig);

                // request subscription
                var sub = client.Requests
                    .ObserveOn(TaskPoolScheduler.Default)
                    .Subscribe(async tsRequest =>
                    {
                        var request = tsRequest.Value;
                        logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Received request {request.Method} [{request.Id}]");

                        try
                        {
                            // boot pre-connected clients
                            if (banManager?.IsBanned(client.RemoteEndpoint.Address) == true)
                            {
                                logger.Trace(() => $"[{LogCat}] [{connectionId}] Disconnecting banned client @ {remoteEndPoint.Address}");
                                DisconnectClient(client);
                                return;
                            }

                            await OnRequestAsync(client, tsRequest);
                        }

                        catch (Exception ex)
                        {
                            logger.Error(ex, () => $"Error handling request: {request.Method}");
                        }
                    }, ex => OnReceiveError(client, ex), () => OnReceiveComplete(client));

                lock (clients)
                {
                    clients[connectionId] = Tuple.Create(client, sub);
                }

                OnConnect(client);
            }

            catch (Exception ex)
            {
                logger.Error(ex, () => nameof(OnClientConnected));
            }
        }

        private void OnReceiveError(StratumClient<TClientContext> client, Exception ex)
        {
            var opEx = ex as OperationException;

            // log everything but ECONNRESET which just indicates the client disconnecting
            if (opEx?.ErrorCode != ErrorCode.ECONNRESET)
                logger.Error(() => $"[{LogCat}] [{client.ConnectionId}] Connection error state: {ex.Message}");

            DisconnectClient(client);
        }

        private void OnReceiveComplete(StratumClient<TClientContext> client)
        {
            logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Received EOF");

            DisconnectClient(client);
        }

        protected void DisconnectClient(StratumClient<TClientContext> client)
        {
            Contract.RequiresNonNull(client, nameof(client));

            var subscriptionId = client.ConnectionId;

            if (!string.IsNullOrEmpty(subscriptionId))
                lock (clients)
                {
                    Tuple<StratumClient<TClientContext>, IDisposable> item;
                    if (clients.TryGetValue(subscriptionId, out item))
                    {
                        item.Item2.Dispose();
                        clients.Remove(subscriptionId);
                    }
                }

            client.Disconnect();

            OnDisconnect(subscriptionId);
        }

        protected void ForEachClient(Action<StratumClient<TClientContext>> action)
        {
            StratumClient<TClientContext>[] tmp;

            lock (clients)
            {
                tmp = clients.Values.Select(x => x.Item1).ToArray();
            }

            foreach (var client in tmp)
                action(client);
        }

        protected abstract void OnConnect(StratumClient<TClientContext> client);
        protected abstract void OnDisconnect(string subscriptionId);

        protected abstract Task OnRequestAsync(StratumClient<TClientContext> client,
            Timestamped<JsonRpcRequest> request);
    }
}
