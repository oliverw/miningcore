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
            public void Run(ILibuvTrace tracer, IPEndPoint endPoint, UvAsyncHandle stopHandle)
            {
                try
                {
                    var loop = new UvLoopHandle(tracer);
                    loop.Init(new LibuvFunctions());

                    stopHandle.Init(loop, () =>
                    {
                        loop.Stop();
                    }, null);

                    var server = new UvTcpHandle(tracer);
                    server.Init(loop, null);

                    server.Bind(endPoint);
                    server.Listen(LibuvConstants.ListenBacklog, OnNewConnection, null);

                    loop.Run();

                    server.Dispose();
                    loop.Dispose();
                }

                catch (Exception ex)
                {
                    tracer.LogError(ex.ToString());
                    Console.WriteLine(ex);
                }
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

            ConcurrentQueue<Tuple<Action<IntPtr>, IntPtr>> handles = new ConcurrentQueue<Tuple<Action<IntPtr>, IntPtr>>();
            Action<Action<IntPtr>, IntPtr> queueCloseHandle = (action, ptr) =>
            {
                handles.Enqueue(Tuple.Create(action, ptr));
            };

            // handle ctrl+c
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                server.Stop();
            };

            // this blocks
            server.Run(tracer, endPoint);

            // cleanup
            Tuple<Action<IntPtr>, IntPtr> item;
            while (handles.TryDequeue(out item))
                item.Item1(item.Item2);
        }
    }
}
