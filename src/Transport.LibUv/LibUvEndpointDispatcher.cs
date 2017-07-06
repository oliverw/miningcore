using System;
using System.Net;
using Autofac;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using Microsoft.Extensions.Logging;
using MiningCore.Extensions;

namespace MiningCore.Transport.LibUv
{
    public class LibUvEndpointDispatcher : IEndpointDispatcher
    {
        public LibUvEndpointDispatcher(ILogger<LibUvEndpointDispatcher> logger, IComponentContext ctx)
        {
            this.logger = logger;
            this.ctx = ctx;
            this.tracer = new LibuvTrace(logger);
        }

        internal readonly ILibuvTrace tracer;
        internal UvLoopHandle loop;
        private UvAsyncHandle stopEvent;
        internal LibuvFunctions uv;
        private readonly ILogger<LibUvEndpointDispatcher> logger;
        private readonly IComponentContext ctx;

        public string EndpointId { get; set; }

        public void Start(IPEndPoint endPoint, Action<IConnection> connectionHandlerFactory)
        {
            try
            {
                loop = new UvLoopHandle(tracer);

                uv = new LibuvFunctions();
                stopEvent = new UvAsyncHandle(tracer);

                loop.Init(uv);

                stopEvent.Init(loop, () =>
                {
                    // ReSharper disable once AccessToDisposedClosure
                    loop.Stop();
                }, null);

                var socket = new UvTcpHandle(tracer);
                socket.Init(loop, null);
                socket.Bind(endPoint);

                var listenState = Tuple.Create(this, connectionHandlerFactory);
                socket.Listen(LibuvConstants.ListenBacklog, OnNewConnection, listenState);

                logger.Info(() => $"Listening on {endPoint}");

                loop.Run();

                logger.Info(() => $"Stopped listening on {endPoint}");

                // close handles
                uv.walk(loop, (handle, state) => uv.close(handle, null), IntPtr.Zero);

                // invoke handle-close-callbacks
                loop.Run();

                // done
                loop.Dispose();
            }

            catch (Exception ex)
            {
                tracer.LogError(ex.ToString());
                throw;
            }
        }

        public void Stop()
        {
            stopEvent?.Send();
        }

        private static void OnNewConnection(UvStreamHandle server, int status, UvException ex, object _state)
        {
            var state = (Tuple<LibUvEndpointDispatcher, Action<IConnection>>)_state;
            var self = state.Item1;

            if (status >= 0)
            {
                var con = new LibUvConnection(self.ctx, self, (UvTcpHandle) server, state.Item2);
                con.Init();
            }

            else
                self.tracer.ConnectionError("-1", ex);
        }
    }
}
