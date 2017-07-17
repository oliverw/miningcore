using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiningForce.Configuration;
using MiningForce.Configuration.Extensions;
using MiningForce.MininigPool;
using MiningForce.Stratum;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog.Extensions.Logging;

namespace MiningForce
{
    class Program
    {
        private static IContainer container;
        private static AutofacServiceProvider serviceProvider;
        private static ILogger<Program> logger;
        private static readonly List<StratumServer> servers = new List<StratumServer>();

        static void Main(string[] args)
        {
            try
            {
                string configFile;
                if (!HandleCommandLineOptions(args, out configFile))
                    return;

                Logo();
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

        private static void Logo()
        {
            Console.WriteLine($@"
    __  ____       _             ______                   
   /  |/  (_)___  (_)___  ____ _/ ____/___  _____________ 
  / /|_/ / / __ \/ / __ \/ __ `/ /_  / __ \/ ___/ ___/ _ \
 / /  / / / / / / / / / / /_/ / __/ / /_/ / /  / /__/  __/
/_/  /_/_/_/ /_/_/_/ /_/\__, /_/    \____/_/   \___/\___/ 
                       /____/                             
");
            Console.WriteLine($"Copyright (c) 2017 poolmining.org\n");
            Console.WriteLine($"Please contribute to the development of the project by donating:\n");
            Console.WriteLine($"BTC - 17QnVor1B6oK1rWnVVBrdX9gFzVkZZbhDm");
            Console.WriteLine($"ETH - 0xcb55abBfe361B12323eb952110cE33d5F28BeeE1");
            Console.WriteLine($"LTC - LTK6CWastkmBzGxgQhTTtCUjkjDA14kxzC");
            Console.WriteLine($"XMR - 475YVJbPHPedudkhrcNp1wDcLMTGYusGPF5fqE7XjnragVLPdqbCHBdZg3dF4dN9hXMjjvGbykS6a77dTAQvGrpiQqHp2eH");
            Console.WriteLine();
        }


        private static async void Start(PoolClusterConfig config)
        {
            try
            {
                foreach (var poolConfig in config.Pools)
                {
                    var pool = container.Resolve<Pool>();
                    await pool.StartAsync(poolConfig);
                    servers.Add(pool);
                }
            }

            catch (PoolStartupAbortException ex)
            {
                logger.Error(() => ex.Message);

                logger.Error(() => $"Cluster startup aborted. Good Bye!");

            }

            catch (Exception ex)
            {
                logger.Error(() => $"Error starting cluster", ex);
            }
        }
    }
}
