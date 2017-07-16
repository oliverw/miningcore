using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiningCore.Configuration;
using MiningCore.Configuration.Extensions;
using MiningCore.MiningPool;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog.Extensions.Logging;

namespace MiningCore
{
    class Program
    {
        private static IContainer container;
        private static AutofacServiceProvider serviceProvider;
        private static ILogger<Program> logger;
        private static readonly List<Pool> pools = new List<Pool>();

        static void Main(string[] args)
        {
            try
            {
                string configFile;
                if (!HandleCommandLineOptions(args, out configFile))
                    return;

                Bootstrap();
                var config = ReadConfig(configFile);

                // go
                Start(config);

                Console.ReadLine();
            }

            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private static bool HandleCommandLineOptions(string[] args, out string configFile)
        {
            configFile = null;

            var app = new CommandLineApplication(false)
            {
                FullName = "MiningCore - Mining Pool Engine",
                ShortVersionGetter = () => $"v{Assembly.GetEntryAssembly().GetName().Version}",
                LongVersionGetter = () => $"v{Assembly.GetEntryAssembly().GetName().Version}"
            };

            var versionOption = app.Option("-v|--version", "Version Information", CommandOptionType.NoValue);
            var configFileOption = app.Option("-c|--config <configfile>", "Configuration File", CommandOptionType.SingleValue);
            app.HelpOption("-? | -h | --help");

            app.Execute(args);

            if (versionOption.HasValue())
            {
                app.ShowVersion();
                return false;
            }

            if (!configFileOption.HasValue())
            {
                app.ShowHelp();
                return false;
            }

            configFile = configFileOption.Value();

            return true;
        }

        private static void Bootstrap()
        {
            // Configure DI
            var services = new ServiceCollection()
                .AddLogging();

            var builder = new ContainerBuilder();
            builder.Populate(services);

            builder.RegisterAssemblyModules(new[]
            {
                typeof(AutofacModule).GetTypeInfo().Assembly,
            });

            container = builder.Build();

            serviceProvider = new AutofacServiceProvider(container);

            // Congfigure logging
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>()
                .AddNLog();

            loggerFactory.ConfigureNLog("nlog.config");

            // Done
            logger = container.Resolve<ILogger<Program>>();
            logger.Info(()=> "MiningCore startup ...");
        }

        private static PoolClusterConfig ReadConfig(string file)
        {
            try
            {
                logger.Info(() => $"Reading configuration file {file}");

                var serializer = JsonSerializer.Create(new JsonSerializerSettings()
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });

                using (var reader = new StreamReader(file, System.Text.Encoding.UTF8))
                {
                    using (var jsonReader = new JsonTextReader(reader))
                    {
                        return serializer.Deserialize<Configuration.PoolClusterConfig>(jsonReader);
                    }
                }
            }

            catch (JsonException ex)
            {
                logger.Error(()=> $"Error parsing config: {ex.Message}");
                throw;
            }

            catch (IOException ex)
            {
                logger.Error(() => $"Error parsing config: {ex.Message}");
                throw;
            }
        }

        private static async void Start(PoolClusterConfig config)
        {
            foreach (var poolConfig in config.Pools)
            {
                var pool = container.Resolve<Pool>();
                await pool.StartAsync(poolConfig, config);
                pools.Add(pool);
            }
        }
    }
}
