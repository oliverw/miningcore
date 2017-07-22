using System;
using System.Net;
using CodeContracts;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using NLog;

namespace LibUvManaged
{
    public class LibUvListener
    {
        public LibUvListener()
        {
	        this.tracer = new LibuvTrace();
        }

        internal readonly ILibuvTrace tracer;
        internal UvLoopHandle loop;
        private UvAsyncHandle stopEvent;
        internal LibuvFunctions uv;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

	    public string EndpointId { get; set; }

        public void Start(IPEndPoint endPoint, Action<ILibUvConnection> connectionHandler)
        {
            Contract.RequiresNonNull(endPoint, nameof(endPoint));
            Contract.RequiresNonNull(connectionHandler, nameof(connectionHandler));

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

                var listenState = Tuple.Create(this, connectionHandler);
                socket.Listen(LibuvConstants.ListenBacklog, OnNewConnection, listenState);

	            logger.Debug(() => $"Listening on {endPoint}");

                loop.Run();

	            logger.Debug(() => $"Stopped listening on {endPoint}");

                // close handles
                uv.walk(loop, (handle, state) => uv.close(handle, null), IntPtr.Zero);

                // invoke handle-close-callbacks
                loop.Run();

                // done
                loop.Close();
            }

            catch (Exception ex)
            {
	            logger.Error(ex);
                throw;
            }
        }

        public void Stop()
        {
            stopEvent?.Send();
        }

        private static void OnNewConnection(UvStreamHandle server, int status, UvException ex, object _state)
        {
            var state = (Tuple<LibUvListener, Action<ILibUvConnection>>)_state;
            var self = state.Item1;
            var handler = state.Item2;

            if (status >= 0)
            {
                // intialize new connection
                var con = new LibUvConnection(self, (UvTcpHandle) server);
                con.Init();

                // hand it off to handler
                handler(con);
            }

            else
                self.tracer.ConnectionError("-1", ex);
        }
    }
}
