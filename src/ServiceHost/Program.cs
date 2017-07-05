using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;

// https://nikhilm.github.io/uvbook/basics.html

namespace ServiceHost
{
    class Program
    {
        public class Server
        {
            private UvAsyncHandle stopEvent;

            public void Run(ILibuvTrace tracer, IPEndPoint endPoint)
            {
                try
                {
                    var loop = new UvLoopHandle(tracer);

                    var uv = new LibuvFunctions();
                    stopEvent = new UvAsyncHandle(tracer);
                    var socket = new UvTcpHandle(tracer);

                    loop.Init(uv);

                    stopEvent.Init(loop, () =>
                    {
                        loop.Stop();
                    }, null);

                    socket.Init(loop, null);

                    socket.Bind(endPoint);
                    socket.Listen(LibuvConstants.ListenBacklog, OnNewConnection, null);

                    loop.Run();

                    // close handles
                    socket.Dispose();
                    stopEvent.Dispose();

                    // enter runloop again to invoke close callbacks on the handles
                    loop.Run();

                    // finally done
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
                stopEvent.Send();
            }

            private static void OnNewConnection(UvStreamHandle streamHandle, int status, UvException ex, object state)
            {
            }
        }

        static void Main(string[] args)
        {
            var logger = new DebugLogger("default");
            var tracer = new LibuvTrace(logger);
            var endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 57000);
            var server = new Server();

            // handle ctrl+c
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                server.Stop();
            };

            // this blocks
            server.Run(tracer, endPoint);
        }
    }
}
