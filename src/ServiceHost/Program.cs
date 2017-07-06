using System;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Threading;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using NLog.Extensions.Logging;
using Transport;
using Transport.LibUv;

// - Read listener json config file

namespace MiningCore
{
    class Program
    {
        public static IContainer Container { get; private set; }
        private static AutofacServiceProvider serviceProvider;

        class EchoClient : IDisposable
        {
            private IDisposable sub;

            public EchoClient(IConnection connection)
            {
                connection.Output.OnNext(System.Text.Encoding.UTF8.GetBytes("Ready.\n"));

                sub = connection.Input
                    .ObserveOn(ThreadPoolScheduler.Instance)
                    .SubscribeOn(ThreadPoolScheduler.Instance)
                    .Subscribe(x =>
                    {
                        var msg = System.Text.Encoding.UTF8.GetString(x);

                        for (int i = 0; i < 20000; i++)
                        {
                            var msg2 = $"{i} - You wrote: {msg}";
                            connection.Output.OnNext(System.Text.Encoding.UTF8.GetBytes(msg2));
                        }
                    });
            }

            public void Dispose()
            {
                sub?.Dispose();
            }
        }

        static void Main(string[] args)
        {
            var logger2 = new DebugLogger("default");
            var listener = (IEndpointDispatcher)new LibUvEndpointDispatcher(logger2);
            EchoClient client = null;

            // handle ctrl+c
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;

                client?.Dispose();
                listener.Stop();
            };

            listener.Start(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 57000), (transport => client = new EchoClient(transport)));


            // Command-Line Options
            var app = new CommandLineApplication(false);
            app.FullName = "MiningCore - Mining Pool Engine";
            app.ShortVersionGetter = () => $"(Build {Assembly.GetEntryAssembly().GetName().Version.Build})";

            var configFile = app.Option("-config|-c <configfile>", "Configuration File", CommandOptionType.SingleValue);
            app.HelpOption("-? | -h | --help");

            if (!configFile.HasValue())
            {
                app.ShowHelp();
                return;
            }

            // Configure DI
            var services  = new ServiceCollection()
                .AddLogging();

            var builder = new ContainerBuilder();
            builder.Populate(services);

            builder.RegisterAssemblyModules(new[]
            {
                typeof(AutofacModule).GetTypeInfo().Assembly,
            });

            Container = builder.Build();

            serviceProvider = new AutofacServiceProvider(Container);

            // Congfigure logging
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>()
                .AddNLog();

            loggerFactory.ConfigureNLog("NLog.config");

            var logger = Container.Resolve<ILogger<Program>>();

            // Done
            logger.LogInformation("MiningCore startup ...");
        }
    }
}

//class EchoClient
//{
//    public EchoClient(IConnection connection)
//    {
//        connection.Output.OnNext(System.Text.Encoding.UTF8.GetBytes("Ready.\n"));

//        connection.Input
//            .ObserveOn(ThreadPoolScheduler.Instance)
//            .Subscribe(x =>
//            {
//                var msg = System.Text.Encoding.UTF8.GetString(x);

//                for (int i = 0; i < 20000; i++)
//                {
//                    var msg2 = $"{i} - You wrote: {msg}";
//                    connection.Output.OnNext(System.Text.Encoding.UTF8.GetBytes(msg2));
//                }
//            });
//    }
//}

            //    var sub = new Subject<int>();
            //sub
            //    .ObserveOn(ThreadPoolScheduler.Instance)
            //    .Subscribe(x =>
            //{
            //    Thread.Sleep(2000);
            //    Console.WriteLine($"Got value {x}");
            //});
            //for (int i = 0; i < 10; i++)
            //{
            //    sub.OnNext(i);
            //    Console.WriteLine($"Produced value {i}");
            //}

            //Console.ReadLine();
            //return;
