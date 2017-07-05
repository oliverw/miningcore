using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Transport;

// - Read listener json config file

namespace MiningCore
{
    class Program
    {
        public static IContainer Container { get; private set; }
        private static AutofacServiceProvider serviceProvider;

        static void Main(string[] args)
        {
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
