using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using MiningForce.Configuration;
using MiningForce.MininigPool;
using MiningForce.Persistence;
using MiningForce.Stratum;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog.Conditions;
using NLog.Config;
using NLog.Targets;

namespace MiningForce
{
    class Program
    {
        private static IContainer container;
        private static AutofacServiceProvider serviceProvider;
        private static ILogger logger;
        private static readonly List<StratumServer> servers = new List<StratumServer>();
	    private static CommandOption dumpConfigOption;
	    private static SharePersister sharePersister;


		static void Main(string[] args)
        {
            try
            {
				string configFile;
                if (!HandleCommandLineOptions(args, out configFile))
                    return;

                Logo();
                var config = ReadConfig(configFile);

	            if (dumpConfigOption.HasValue())
	            {
		            Console.WriteLine("\nCurrent configuration as parsed from config file:");

					Console.WriteLine(JsonConvert.SerializeObject(config, new JsonSerializerSettings
					{
						ContractResolver = new CamelCasePropertyNamesContractResolver(),
						Formatting = Formatting.Indented
					}));

					return;
	            }

	            Bootstrap(config);

				// go
				Start(config);

                Console.ReadLine();
            }

            catch (PoolStartupAbortException ex)
            {
	            Console.WriteLine(ex.Message);

				Console.WriteLine("\nCluster cannot start. Good Bye!");
			}

			catch (JsonException)
            {
				// ignored
            }

            catch (IOException)
            {
	            // ignored
            }

			catch (Exception ex)
            {
                Console.WriteLine(ex);

	            Console.WriteLine("\nCluster cannot start. Good Bye!");
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
	        dumpConfigOption = app.Option("-dc|--dumpconfig", "Dump the configuration (useful for trouble-shooting typos in the config file)", CommandOptionType.NoValue);
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

        private static void Bootstrap(ClusterConfig config)
        {
            // Configure DI
            var services = new ServiceCollection();

            var builder = new ContainerBuilder();
            builder.Populate(services);

            builder.RegisterAssemblyModules(new[]
            {
                typeof(AutofacModule).GetTypeInfo().Assembly,
            });

	        ConfigurePersistence(config, builder);
			container = builder.Build();

            serviceProvider = new AutofacServiceProvider(container);
            ConfigureLogging(config.Logging);
        }

	    private static ClusterConfig ReadConfig(string file)
        {
            try
            {
                Console.WriteLine($"Using configuration file {file}\n");

                var serializer = JsonSerializer.Create(new JsonSerializerSettings()
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });

                using (var reader = new StreamReader(file, System.Text.Encoding.UTF8))
                {
                    using (var jsonReader = new JsonTextReader(reader))
                    {
                        return serializer.Deserialize<ClusterConfig>(jsonReader);
                    }
                }
            }

            catch (JsonException ex)
            {
	            Console.WriteLine($"Error: {ex.Message}");
                throw;
            }

            catch (IOException ex)
            {
                logger.Error(() => $"Error: {ex.Message}");
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

	    private static void ConfigureLogging(ClusterLoggingConfig config)
	    {
			var loggingConfig = new LoggingConfiguration();

		    if (config != null)
		    {
			    // parse level
			    var level = !string.IsNullOrEmpty(config.Level)
				    ? LogLevel.FromString(config.Level)
				    : LogLevel.Info;

			    var layout = "[${longdate}] [${pad:inner=${level:uppercase=true}}] [${logger:shortName=true}] ${message} ${exception:format=ToString,StackTrace}";

			    if (config.EnableConsoleLog)
			    {
				    if (config.EnableConsoleColors)
				    {
					    var target = new ColoredConsoleTarget("console")
					    {
						    Layout = layout
					    };

					    target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
						    ConditionParser.ParseExpression("level == LogLevel.Debug"),
						    ConsoleOutputColor.DarkGray, ConsoleOutputColor.NoChange));

						target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
						    ConditionParser.ParseExpression("level == LogLevel.Debug"),
						    ConsoleOutputColor.Gray, ConsoleOutputColor.NoChange));

					    target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
						    ConditionParser.ParseExpression("level == LogLevel.Info"),
						    ConsoleOutputColor.White, ConsoleOutputColor.NoChange));

					    target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
						    ConditionParser.ParseExpression("level == LogLevel.Warn"),
						    ConsoleOutputColor.Yellow, ConsoleOutputColor.NoChange));

					    target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
						    ConditionParser.ParseExpression("level == LogLevel.Error"),
						    ConsoleOutputColor.Red, ConsoleOutputColor.NoChange));

					    target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
						    ConditionParser.ParseExpression("level == LogLevel.Fatal"),
						    ConsoleOutputColor.DarkRed, ConsoleOutputColor.White));

					    loggingConfig.AddTarget(target);
					    loggingConfig.AddRule(level, LogLevel.Fatal, target);
				    }

					else
				    {
					    var target = new ConsoleTarget("console")
					    {
						    Layout = layout
					    };

					    loggingConfig.AddTarget(target);
					    loggingConfig.AddRule(level, LogLevel.Fatal, target);
				    }
				}

			    if (!string.IsNullOrEmpty(config.LogFile))
			    {
				    var target = new FileTarget("file")
				    {
					    FileName = config.LogFile,
					    FileNameKind = FilePathKind.Unknown,
					    Layout = layout
				    };

				    loggingConfig.AddRule(level, LogLevel.Fatal, target);
			    }
			}

		    LogManager.Configuration = loggingConfig;

		    logger = LogManager.GetCurrentClassLogger();
	    }

	    private static void ConfigurePersistence(ClusterConfig config, ContainerBuilder builder)
	    {
			if(config.Persistence == null)
				throw new PoolStartupAbortException("Persistence is not configured!");

		    if (config.Persistence.Postgres != null)
			    ConfigurePostgres(config.Persistence.Postgres, builder);
	    }

	    private static void ConfigurePostgres(DatabaseConfig pgConfig, ContainerBuilder builder)
	    {
		    // validate config
			if (string.IsNullOrEmpty(pgConfig.Host))
			    throw new PoolStartupAbortException("Postgres configuration: invalid or missing 'host'");

		    if (pgConfig.Port == 0)
			    throw new PoolStartupAbortException("Postgres configuration: invalid or missing 'port'");

			if (string.IsNullOrEmpty(pgConfig.Database))
			    throw new PoolStartupAbortException("Postgres configuration: invalid or missing 'database'");

		    if (string.IsNullOrEmpty(pgConfig.User))
			    throw new PoolStartupAbortException("Postgres configuration: invalid or missing 'user'");

		    // build connection string
		    var connectionString = $"Server={pgConfig.Host};Port={pgConfig.Port};Database={pgConfig.Database};User Id={pgConfig.User};Password={pgConfig.Password};";

			// register connection factory
		    builder.RegisterInstance(new Persistence.Postgres.ConnectionFactory(connectionString));

			// register repositories
		    builder.RegisterType<Persistence.Postgres.Repositories.ShareRepository>()
			    .AsImplementedInterfaces()
				.SingleInstance();
		}

		private static async void Start(ClusterConfig config)
		{
			// start share persister
			sharePersister = container.Resolve<SharePersister>();
			sharePersister.Start();

			// start pools
			foreach (var poolConfig in config.Pools.Where(x=> x.Enabled))
            {
                var pool = container.Resolve<Pool>(
					new TypedParameter(typeof(PoolConfig), poolConfig),
	                new TypedParameter(typeof(ClusterConfig), config));

	            sharePersister.AttachPool(pool);

				await pool.StartAsync();
                servers.Add(pool);
            }
        }
    }
}
