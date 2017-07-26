using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using AutoMapper;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using MiningForce.Configuration;
using MiningForce.Crypto.Hashing;
using MiningForce.Crypto.Hashing.Algorithms;
using MiningForce.Extensions;
using MiningForce.MininigPool;
using MiningForce.Payments;
using MiningForce.Persistence;
using MiningForce.Stratum;
using MiningForce.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog.Conditions;
using NLog.Config;
using NLog.Layouts;
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
	    private static ShareRecorder shareRecorder;
	    private static PaymentProcessor paymentProcessor;

		static void Main(string[] args)
		{
            try
            {
#if DEBUG
	            DebugLoadMultiHashNativeWorkaround();
#endif
				string configFile;
                if (!HandleCommandLineOptions(args, out configFile))
                    return;

                Logo();
                var clusterConfig = ReadConfig(configFile);

	            if (dumpConfigOption.HasValue())
	            {
		            DumpParsedConfig(clusterConfig);
		            return;
	            }

	            ValidateConfig(clusterConfig);
	            Bootstrap(clusterConfig);
				Start(clusterConfig).Wait();

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

            catch (AggregateException ex)
            {
	            if (!(ex.InnerExceptions.First() is PoolStartupAbortException))
		            Console.WriteLine(ex);

				Console.WriteLine("Cluster cannot start. Good Bye!");
            }

			catch (Exception ex)
            {
                Console.WriteLine(ex);

	            Console.WriteLine("Cluster cannot start. Good Bye!");
            }
		}

	    private static void DumpParsedConfig(ClusterConfig config)
	    {
		    Console.WriteLine("\nCurrent configuration as parsed from config file:");

		    Console.WriteLine(JsonConvert.SerializeObject(config, new JsonSerializerSettings
		    {
			    ContractResolver = new CamelCasePropertyNamesContractResolver(),
			    Formatting = Formatting.Indented
		    }));
	    }

	    private static bool HandleCommandLineOptions(string[] args, out string configFile)
        {
            configFile = null;

            var app = new CommandLineApplication(false)
            {
                FullName = "MiningForce - Mining Pool Engine",
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
            // Service collection
            var services = new ServiceCollection();

            var builder = new ContainerBuilder();
            builder.Populate(services);

            builder.RegisterAssemblyModules(new[]
            {
                typeof(AutofacModule).GetTypeInfo().Assembly,
            });

	        // AutoMapper
	        var amConf = new MapperConfiguration(cfg =>
	        {
		        cfg.AddProfile(new AutoMapperProfile());
	        });

	        builder.Register((ctx, parms) => amConf.CreateMapper());

			// Persistence
			ConfigurePersistence(config, builder);

			// Autofac Container
			container = builder.Build();
            serviceProvider = new AutofacServiceProvider(container);

			// Logging
            ConfigureLogging(config);

			ValidateRuntimeEnvironment();
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

		private static void ValidateConfig(ClusterConfig clusterConfig)
	    {
		    if (clusterConfig.Pools.Length == 0)
			    logger.ThrowLogPoolStartupException("No pools configured!");

		    ValidatePoolIds(clusterConfig);
	    }

	    private static void ValidatePoolIds(ClusterConfig clusterConfig)
	    {
		    // check for missing ids
		    if (clusterConfig.Pools.Any(pool => string.IsNullOrEmpty(pool.Id)))
			    logger.ThrowLogPoolStartupException($"Pool {clusterConfig.Pools.ToList().IndexOf(clusterConfig.Pools.First(pool => string.IsNullOrEmpty(pool.Id)))} has an empty id!");

		    // check for duplicate ids
		    var ids = clusterConfig.Pools
			    .GroupBy(x => x.Id)
			    .ToArray();

		    if (ids.Any(id => id.Count() > 1))
			    logger.ThrowLogPoolStartupException($"Duplicate pool id '{ids.First(id => id.Count() > 1).Key}'!");
	    }

	    private static void ValidateRuntimeEnvironment()
	    {
		    // root check
		    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Environment.UserName == "root")
			    logger.Warn(() => "Running as root is discouraged!");
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

	    private static void ConfigureLogging(ClusterConfig clusterConfig)
	    {
			var config = clusterConfig.Logging;
			var loggingConfig = new LoggingConfiguration();

		    if (config != null)
		    {
			    // parse level
			    var level = !string.IsNullOrEmpty(config.Level)
				    ? LogLevel.FromString(config.Level)
				    : LogLevel.Info;

			    var layout = "[${longdate}] [${level:format=FirstCharacter:uppercase=true}] [${logger:shortName=true}] ${message} ${exception:format=ToString,StackTrace}";

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

				    loggingConfig.AddTarget(target);
				    loggingConfig.AddRule(level, LogLevel.Fatal, target);
			    }

			    if (config.PerPoolLogFile)
			    {
				    foreach (var poolConfig in clusterConfig.Pools)
				    {
					    var target = new FileTarget(poolConfig.Id)
					    {
						    FileName = GetLogPath(config, poolConfig.Id + ".log"),
						    FileNameKind = FilePathKind.Unknown,
						    Layout = layout
					    };

					    loggingConfig.AddTarget(target);
					    loggingConfig.AddRule(level, LogLevel.Fatal, target, poolConfig.Id);
				    }
				}
			}

		    LogManager.Configuration = loggingConfig;

		    logger = LogManager.GetCurrentClassLogger();
	    }

	    private static Layout GetLogPath(ClusterLoggingConfig config, string name)
	    {
		    if (string.IsNullOrEmpty(config.LogBaseDirectory))
			    return name;

		    return Path.Combine(config.LogBaseDirectory, name);
	    }

	    private static void ConfigurePersistence(ClusterConfig clusterConfig, ContainerBuilder builder)
	    {
			if(clusterConfig.Persistence == null)
				logger.ThrowLogPoolStartupException("Persistence is not configured!");

		    if (clusterConfig.Persistence.Postgres != null)
			    ConfigurePostgres(clusterConfig.Persistence.Postgres, builder);
	    }

	    private static void ConfigurePostgres(DatabaseConfig pgConfig, ContainerBuilder builder)
	    {
		    // validate config
			if (string.IsNullOrEmpty(pgConfig.Host))
			    logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'host'");

		    if (pgConfig.Port == 0)
			    logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'port'");

			if (string.IsNullOrEmpty(pgConfig.Database))
			    logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'database'");

		    if (string.IsNullOrEmpty(pgConfig.User))
			    logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'user'");

		    // build connection string
		    var connectionString = $"Server={pgConfig.Host};Port={pgConfig.Port};Database={pgConfig.Database};User Id={pgConfig.User};Password={pgConfig.Password};";

			// register connection factory
		    builder.RegisterInstance(new Persistence.Postgres.ConnectionFactory(connectionString))
				.AsImplementedInterfaces()
				.SingleInstance();

			// register repositories
			builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
			    .Where(t =>
					t.Namespace.StartsWith(typeof(Persistence.Postgres.Repositories.ShareRepository).Namespace))
			    .AsImplementedInterfaces()
			    .SingleInstance();
		}

		private static async Task Start(ClusterConfig clusterConfig)
		{
			// start share recorder
			shareRecorder = container.Resolve<ShareRecorder>();
			shareRecorder.Start();

			// start pools
			foreach (var poolConfig in clusterConfig.Pools.Where(x=> x.Enabled))
            {
                var pool = container.Resolve<Pool>();
	            pool.Configure(poolConfig, clusterConfig);

				servers.Add(pool);
				shareRecorder.AttachPool(pool);

				await pool.StartAsync();
            }

			// start payment processor
			if (clusterConfig.PaymentProcessing?.Enabled == true &&
			    clusterConfig.Pools.Any(x => x.PaymentProcessing?.Enabled == true))
			{
				paymentProcessor = container.Resolve<PaymentProcessor>();
				paymentProcessor.Configure(clusterConfig);

				paymentProcessor.Start();
			}
		}

	    [DllImport("kernel32.dll", SetLastError = true)]
	    static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, uint dwFlags);

		private static void DebugLoadMultiHashNativeWorkaround()
	    {
#if DEBUG
		    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		    {
			    var runtime = Environment.Is64BitProcess ? "win7-x64" : "win7-86";
			    var appRoot = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

			    var path = Path.Combine(appRoot, "runtimes", runtime, "native", "multihash-native.dll");
			    var result = LoadLibraryEx(path, IntPtr.Zero, 0);
		    }
#endif
		}
	}
}
