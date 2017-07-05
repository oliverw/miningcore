using System;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging.Debug;
using Transport;
using Transport.LibUv;

// https://nikhilm.github.io/uvbook/basics.html

namespace ServiceHost
{
    class Program
    {
        class EchoClient
        {
            public EchoClient(IConnection connection)
            {
                connection.Output.OnNext(System.Text.Encoding.UTF8.GetBytes("Ready.\n"));

                connection.Input
                    .ObserveOn(ThreadPoolScheduler.Instance)
                    .Subscribe(x =>
                {
                    var msg = System.Text.Encoding.UTF8.GetString(x);

                    for (int i = 0; i < 20; i++)
                    {
                        var msg2 = $"{i} - You wrote: {msg}";
                        connection.Output.OnNext(System.Text.Encoding.UTF8.GetBytes(msg2));
                    }
                });
            }
        }

        static void Main(string[] args)
        {
            var logger = new DebugLogger("default");
            var listener = (IListener) new UvListener(logger);

            // handle ctrl+c
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                listener.Stop();
            };

            listener.RegisterEndpoint(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 57000), (transport => new EchoClient(transport)));

            listener.Start();
        }
    }
}
