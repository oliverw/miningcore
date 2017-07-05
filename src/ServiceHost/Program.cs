using System;
using System.Collections.Concurrent;
using System.Net;
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
            private UvLoopHandle loop;

            public UvAsyncHandle Init(ILibuvTrace tracer, IPEndPoint endPoint)
            {
                try
                {
                    loop = new UvLoopHandle(tracer);
                    var uv = new LibuvFunctions();
                    var stopHandle = new UvAsyncHandle(tracer);

                    var server = new UvTcpHandle(tracer);

                    loop.Init(uv);

                    stopHandle.Init(loop, () =>
                    {
                        // Call uv_shutdown and on the shutdown callback uv_close the handle.

                        server.Dispose();
                        loop.Stop();
                    }, null);

                    server.Init(loop, null);

                    server.Bind(endPoint);
                    server.Listen(LibuvConstants.ListenBacklog, OnNewConnection, null);

                    return stopHandle;
                }

                catch (Exception ex)
                {
                    tracer.LogError(ex.ToString());
                    Console.WriteLine(ex);
                    throw;
                }
            }

            public void Run()
            {
                loop.Run();

                loop.Dispose();
            }

            private static void OnNewConnection(UvStreamHandle streamHandle, int status, UvException ex, object state)
            {
            }
        }

        static void Main(string[] args)
        {
            Server server = null;

            var logger = new DebugLogger("default");
            var tracer = new LibuvTrace(logger);
            var endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 57000);

            server = new Server();

            // this blocks
            var stopHandle = server.Init(tracer, endPoint);


            // handle ctrl+c
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                stopHandle.Send();
            };

            server.Run();
        }
    }
}
