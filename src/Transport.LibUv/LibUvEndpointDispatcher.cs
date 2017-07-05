using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using Microsoft.Extensions.Logging;

namespace Transport.LibUv
{
    public class LibUvEndpointDispatcher : IEndpointDispatcher
    {
        public LibUvEndpointDispatcher(ILogger logger)
        {
            tracer = new LibuvTrace(logger);
        }

        internal readonly ILibuvTrace tracer;
        internal UvLoopHandle loop;
        private UvAsyncHandle stopEvent;
        private readonly Dictionary<IPEndPoint, Action<IConnection>> endPoints =
            new Dictionary<IPEndPoint, Action<IConnection>>();
        internal LibuvFunctions uv;

        public void RegisterEndpoint(IPEndPoint endPoint, Action<IConnection> connectionHandlerFactory)
        {
            endPoints[endPoint] = connectionHandlerFactory;
        }

        public void Start()
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

                foreach (var endPoint in endPoints.Keys)
                {
                    var socket = new UvTcpHandle(tracer);
                    socket.Init(loop, null);
                    socket.Bind(endPoint);

                    var connectionHandler = endPoints[endPoint];
                    var state = Tuple.Create(this, connectionHandler);
                    socket.Listen(LibuvConstants.ListenBacklog, OnNewConnection, state);
                }

                loop.Run();

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
                var con = new LibUvConnection(self, (UvTcpHandle) server, state.Item2);
                con.Init();
            }

            else
                self.tracer.ConnectionError("-1", ex);
        }
    }
}
