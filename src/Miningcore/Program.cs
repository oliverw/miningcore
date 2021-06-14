/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AspNetCoreRateLimit;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Features.Metadata;
using AutoMapper;
using FluentValidation;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Miningcore.Api;
using Miningcore.Api.Controllers;
using Miningcore.Api.Middlewares;
using Miningcore.Api.Responses;
using Miningcore.Configuration;
using Miningcore.Crypto.Hashing.Equihash;
using Miningcore.Extensions;
using Miningcore.Mining;
using Miningcore.Native;
using Miningcore.Notifications;
using Miningcore.Payments;
using Miningcore.Persistence.Dummy;
using Miningcore.Persistence.Postgres;
using Miningcore.Persistence.Postgres.Repositories;
using Miningcore.Util;
using NBitcoin.Zcash;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using NLog.Conditions;
using NLog.Config;
using NLog.Extensions.Hosting;
using NLog.Extensions.Logging;
using NLog.Layouts;
using NLog.Targets;
using Prometheus;
using WebSocketManager;
using ILogger = NLog.ILogger;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

// ReSharper disable AssignNullToNotNullAttribute
// ReSharper disable PossibleNullReferenceException

namespace Miningcore
{
    public class Program : BackgroundService
    {
        public static async Task Main(string[] args)
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

                if(!ParseCommandLine(args, out var configFile))
                    return;

                isShareRecoveryMode = shareRecoveryOption.HasValue();

                Logo();
                clusterConfig = ReadConfig(configFile);

                if(dumpConfigOption.HasValue())
                {
                    DumpParsedConfig(clusterConfig);
                    return;
                }

                ValidateConfig();
                ConfigureLogging();
                LogRuntimeInfo();
                ConfigureMisc();
                ValidateRuntimeEnvironment();

                var hostBuilder = new HostBuilder();

                hostBuilder
                    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                    .ConfigureContainer((Action<ContainerBuilder>) ConfigureAutofac)
                    .UseNLog()
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.AddNLog();
                        logging.SetMinimumLevel(LogLevel.Trace);
                    })
                    .ConfigureServices((ctx, services) =>
                    {
                        services.AddHostedService<Program>();
                    });

                if(clusterConfig.Api == null || clusterConfig.Api.Enabled)
                {
                    var address = clusterConfig.Api?.ListenAddress != null
                        ? (clusterConfig.Api.ListenAddress != "*" ? IPAddress.Parse(clusterConfig.Api.ListenAddress) : IPAddress.Any)
                        : IPAddress.Parse("127.0.0.1");

                    var port = clusterConfig.Api?.Port ?? 4000;
                    var enableApiRateLimiting = clusterConfig.Api?.RateLimiting?.Disabled != true;

                    hostBuilder.ConfigureWebHost(builder =>
                    {
                        builder.ConfigureServices(services =>
                        {
                            // rate limiting
                            if(enableApiRateLimiting)
                            {
                                services.Configure<IpRateLimitOptions>(ConfigureIpRateLimitOptions);
                                services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
                                services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
                                services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
                            }

                            services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();

                            // Controllers
                            services.AddSingleton<PoolApiController, PoolApiController>();
                            services.AddSingleton<AdminApiController, AdminApiController>();

                            // MVC
                            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

                            services.AddMvc(options => { options.EnableEndpointRouting = false; })
                                .SetCompatibilityVersion(CompatibilityVersion.Latest)
                                .AddControllersAsServices()
                                .AddJsonOptions(options => { options.JsonSerializerOptions.WriteIndented = true; });

                            // Gzip Compression
                            services.AddResponseCompression();

                            // Cors
                            services.AddCors();

                            // WebSockets
                            services.AddWebSocketManager();
                        })
                        .UseKestrel(options =>
                        {
                            options.Listen(address, port, listenOptions =>
                            {
                                if(clusterConfig.Api.SSLConfig?.Enabled == true)
                                    listenOptions.UseHttps(clusterConfig.Api.SSLConfig.SSLPath, clusterConfig.Api.SSLConfig.SSLPassword);
                            });
                        })
                        .Configure(app =>
                        {
                            if(enableApiRateLimiting)
                                app.UseIpRateLimiting();

                            app.UseMiddleware<ApiExceptionHandlingMiddleware>();

                            UseIpWhiteList(app, true, new[] { "/api/admin" }, clusterConfig.Api?.AdminIpWhitelist);
                            UseIpWhiteList(app, true, new[] { "/metrics" }, clusterConfig.Api?.MetricsIpWhitelist);

                            app.UseResponseCompression();
                            app.UseCors(corsPolicyBuilder => corsPolicyBuilder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
                            app.UseWebSockets();
                            app.MapWebSocketManager("/notifications", app.ApplicationServices.GetService<WebSocketNotificationsRelay>());
                            app.UseMetricServer();
                            app.UseMvc();

                        });

                        logger.Info(() => $"Prometheus Metrics {address}:{port}/metrics");
                        logger.Info(() => $"WebSocket notifications streaming {address}:{port}/notifications");
                    });
                }

                await hostBuilder.RunConsoleAsync();
            }

            catch(PoolStartupAbortException ex)
            {
                if(!string.IsNullOrEmpty(ex.Message))
                    Console.WriteLine(ex.Message);

                Console.WriteLine("\nCluster cannot start. Good Bye!");
            }

            catch(JsonException)
            {
                // ignored
            }

            catch(IOException)
            {
                // ignored
            }

            catch(AggregateException ex)
            {
                if(ex.InnerExceptions.First() is not PoolStartupAbortException)
                    Console.WriteLine(ex);

                Console.WriteLine("Cluster cannot start. Good Bye!");
            }

            catch(OperationCanceledException)
            {
                // Ctrl+C
            }

            catch(Exception ex)
            {
                Console.WriteLine(ex);

                Console.WriteLine("Cluster cannot start. Good Bye!");
            }
        }

        private readonly IComponentContext container;
        private static ILogger logger;
        private static CommandOption dumpConfigOption;
        private static CommandOption shareRecoveryOption;
        private static bool isShareRecoveryMode;
        private ShareRecorder shareRecorder;
        private ShareRelay shareRelay;
        private ShareReceiver shareReceiver;
        private PayoutManager payoutManager;
        private StatsRecorder statsRecorder;
        private static ClusterConfig clusterConfig;
        private NotificationService notificationService;
        private MetricsPublisher metricsPublisher;
        private BtStreamReceiver btStreamReceiver;
        private static readonly ConcurrentDictionary<string, IMiningPool> pools = new();

        private static readonly AdminGcStats gcStats = new();
        private static readonly Regex regexJsonTypeConversionError =
            new("\"([^\"]+)\"[^\']+\'([^\']+)\'.+\\s(\\d+),.+\\s(\\d+)", RegexOptions.Compiled);

        public Program(IComponentContext container)
        {
            this.container = container;
        }

        private static void ConfigureAutofac(ContainerBuilder builder)
        {
            builder.RegisterAssemblyModules(typeof(AutofacModule).GetTypeInfo().Assembly);
            builder.RegisterInstance(clusterConfig);
            builder.RegisterInstance(pools);
            builder.RegisterInstance(gcStats);

            // AutoMapper
            var amConf = new MapperConfiguration(cfg => { cfg.AddProfile(new AutoMapperProfile()); });
            builder.Register((ctx, parms) => amConf.CreateMapper());

            ConfigurePersistence(builder);
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            if(!isShareRecoveryMode)
            {
                MonitorGc();

                var coinTemplates = LoadCoinTemplates();
                logger.Info($"{coinTemplates.Keys.Count} coins loaded from {string.Join(", ", clusterConfig.CoinTemplates)}");

                // Populate pool configs with corresponding template
                foreach(var poolConfig in clusterConfig.Pools.Where(x => x.Enabled))
                {
                    // Lookup coin definition
                    if(!coinTemplates.TryGetValue(poolConfig.Coin, out var template))
                        logger.ThrowLogPoolStartupException($"Pool {poolConfig.Id} references undefined coin '{poolConfig.Coin}'");

                    poolConfig.Template = template;
                }

                // Notifications
                notificationService = container.Resolve<NotificationService>();

                // start btStream receiver
                btStreamReceiver = container.Resolve<BtStreamReceiver>();
                btStreamReceiver.Start(clusterConfig);

                if(clusterConfig.ShareRelay == null)
                {
                    // start share recorder
                    shareRecorder = container.Resolve<ShareRecorder>();
                    shareRecorder.Start(clusterConfig);

                    // start share receiver (for external shares)
                    shareReceiver = container.Resolve<ShareReceiver>();
                    shareReceiver.Start(clusterConfig);
                }

                else
                {
                    // start share relay
                    shareRelay = container.Resolve<ShareRelay>();
                    shareRelay.Start(clusterConfig);
                }

                // start API
                if(clusterConfig.Api == null || clusterConfig.Api.Enabled)
                {
                    metricsPublisher = container.Resolve<MetricsPublisher>();
                }

                // start payment processor
                if(clusterConfig.PaymentProcessing?.Enabled == true &&
                   clusterConfig.Pools.Any(x => x.PaymentProcessing?.Enabled == true))
                {
                    payoutManager = container.Resolve<PayoutManager>();
                    payoutManager.Configure(clusterConfig);

                    payoutManager.Start();
                }

                else
                    logger.Info("Payment processing is not enabled");

                if(clusterConfig.ShareRelay == null)
                {
                    // start pool stats updater
                    statsRecorder = container.Resolve<StatsRecorder>();
                    statsRecorder.Configure(clusterConfig);
                    statsRecorder.Start();
                }

                // start pools
                await Task.WhenAll(clusterConfig.Pools.Where(x => x.Enabled).Select(async poolConfig =>
                {
                    // resolve pool implementation
                    var poolImpl = container.Resolve<IEnumerable<Meta<Lazy<IMiningPool, CoinFamilyAttribute>>>>()
                        .First(x => x.Value.Metadata.SupportedFamilies.Contains(poolConfig.Template.Family)).Value;

                    // create and configure
                    var pool = poolImpl.Value;
                    pool.Configure(poolConfig, clusterConfig);
                    pools[poolConfig.Id] = pool;

                    // pre-start attachments
                    shareReceiver?.AttachPool(pool);
                    statsRecorder?.AttachPool(pool);

                    await pool.StartAsync(ct);
                }));
            }

            else
                await RecoverSharesAsync(shareRecoveryOption.Value());
        }

        private Task RecoverSharesAsync(string recoveryFilename)
        {
            shareRecorder = container.Resolve<ShareRecorder>();
            return shareRecorder.RecoverSharesAsync(clusterConfig, recoveryFilename);
        }

        private static void LogRuntimeInfo()
        {
            logger.Info(() => $"{RuntimeInformation.FrameworkDescription.Trim()} on {RuntimeInformation.OSDescription.Trim()} [{RuntimeInformation.ProcessArchitecture}]");
        }

        private static void ValidateConfig()
        {
            // set some defaults
            foreach(var config in clusterConfig.Pools)
            {
                config.EnableInternalStratum ??= clusterConfig.ShareRelays == null || clusterConfig.ShareRelays.Length == 0;
            }

            try
            {
                clusterConfig.Validate();
            }

            catch(ValidationException ex)
            {
                Console.WriteLine($"Configuration is not valid:\n\n{string.Join("\n", ex.Errors.Select(x => "=> " + x.ErrorMessage))}");
                throw new PoolStartupAbortException(string.Empty);
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

        private static bool ParseCommandLine(string[] args, out string configFile)
        {
            configFile = null;

            var app = new CommandLineApplication
            {
                FullName = "MiningCore - Pool Mining Engine",
                ShortVersionGetter = () => $"v{Assembly.GetEntryAssembly().GetName().Version}",
                LongVersionGetter = () => $"v{Assembly.GetEntryAssembly().GetName().Version}"
            };

            var versionOption = app.Option("-v|--version", "Version Information", CommandOptionType.NoValue);
            var configFileOption = app.Option("-c|--config <configfile>", "Configuration File",
                CommandOptionType.SingleValue);
            dumpConfigOption = app.Option("-dc|--dumpconfig",
                "Dump the configuration (useful for trouble-shooting typos in the config file)",
                CommandOptionType.NoValue);
            shareRecoveryOption = app.Option("-rs", "Import lost shares using existing recovery file",
                CommandOptionType.SingleValue);
            app.HelpOption("-? | -h | --help");

            app.Execute(args);

            if(versionOption.HasValue())
            {
                app.ShowVersion();
                return false;
            }

            if(!configFileOption.HasValue())
            {
                app.ShowHelp();
                return false;
            }

            configFile = configFileOption.Value();

            return true;
        }

        private static ClusterConfig ReadConfig(string file)
        {
            try
            {
                Console.WriteLine($"Using configuration file {file}\n");

                var serializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });

                using(var reader = new StreamReader(file, Encoding.UTF8))
                {
                    using(var jsonReader = new JsonTextReader(reader))
                    {
                        return serializer.Deserialize<ClusterConfig>(jsonReader);
                    }
                }
            }

            catch(JsonSerializationException ex)
            {
                HumanizeJsonParseException(ex);
                throw;
            }

            catch(JsonException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                throw;
            }

            catch(IOException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                throw;
            }
        }

        private static void HumanizeJsonParseException(JsonSerializationException ex)
        {
            var m = regexJsonTypeConversionError.Match(ex.Message);

            if(m.Success)
            {
                var value = m.Groups[1].Value;
                var type = Type.GetType(m.Groups[2].Value);
                var line = m.Groups[3].Value;
                var col = m.Groups[4].Value;

                if(type == typeof(PayoutScheme))
                    Console.WriteLine($"Error: Payout scheme '{value}' is not (yet) supported (line {line}, column {col})");
            }

            else
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private static void ValidateRuntimeEnvironment()
        {
            // root check
            if(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Environment.UserName == "root")
                logger.Warn(() => "Running as root is discouraged!");

            // require 64-bit on Windows
            if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.ProcessArchitecture == Architecture.X86)
                throw new PoolStartupAbortException("Miningcore requires 64-Bit Windows");
        }

        private static void MonitorGc()
        {
            var thread = new Thread(() =>
            {
                var sw = new Stopwatch();

                while(true)
                {
                    var s = GC.WaitForFullGCApproach();
                    if(s == GCNotificationStatus.Succeeded)
                    {
                        logger.Info(() => "FullGC soon");
                        sw.Start();
                    }

                    s = GC.WaitForFullGCComplete();

                    if(s == GCNotificationStatus.Succeeded)
                    {
                        logger.Info(() => "FullGC completed");

                        sw.Stop();

                        if(sw.Elapsed.TotalSeconds > gcStats.MaxFullGcDuration)
                            gcStats.MaxFullGcDuration = sw.Elapsed.TotalSeconds;

                        sw.Reset();
                    }
                }
            });

            GC.RegisterForFullGCNotification(1, 1);
            thread.Start();
        }

        private static void Logo()
        {
            Console.WriteLine(@"
 ███╗   ███╗██╗███╗   ██╗██╗███╗   ██╗ ██████╗  ██████╗ ██████╗ ██████╗ ███████╗
 ████╗ ████║██║████╗  ██║██║████╗  ██║██╔════╝ ██╔════╝██╔═══██╗██╔══██╗██╔════╝
 ██╔████╔██║██║██╔██╗ ██║██║██╔██╗ ██║██║  ███╗██║     ██║   ██║██████╔╝█████╗
 ██║╚██╔╝██║██║██║╚██╗██║██║██║╚██╗██║██║   ██║██║     ██║   ██║██╔══██╗██╔══╝
 ██║ ╚═╝ ██║██║██║ ╚████║██║██║ ╚████║╚██████╔╝╚██████╗╚██████╔╝██║  ██║███████╗
");
            Console.WriteLine(" https://github.com/coinfoundry/miningcore\n");
            Console.WriteLine(" Please contribute to the development of the project by donating:\n");
            Console.WriteLine(" BTC  - 17QnVor1B6oK1rWnVVBrdX9gFzVkZZbhDm");
            Console.WriteLine(" LTC  - LTK6CWastkmBzGxgQhTTtCUjkjDA14kxzC");
            Console.WriteLine(" DASH - XqpBAV9QCaoLnz42uF5frSSfrJTrqHoxjp");
            Console.WriteLine(" ZEC  - t1YHZHz2DGVMJiggD2P4fBQ2TAPgtLSUwZ7");
            Console.WriteLine(" ZCL  - t1MFU1vD3YKgsK6Uh8hW7UTY8mKAV2xVqBr");
            Console.WriteLine(" ETH  - 0xcb55abBfe361B12323eb952110cE33d5F28BeeE1");
            Console.WriteLine(" ETC  - 0xF8cCE9CE143C68d3d4A7e6bf47006f21Cfcf93c0");
            Console.WriteLine(" XMR  - 475YVJbPHPedudkhrcNp1wDcLMTGYusGPF5fqE7XjnragVLPdqbCHBdZg3dF4dN9hXMjjvGbykS6a77dTAQvGrpiQqHp2eH");
            Console.WriteLine();
        }

        private static void ConfigureLogging()
        {
            var config = clusterConfig.Logging;
            var loggingConfig = new LoggingConfiguration();

            if(config != null)
            {
                // parse level
                var level = !string.IsNullOrEmpty(config.Level)
                    ? NLog.LogLevel.FromString(config.Level)
                    : NLog.LogLevel.Info;

                var layout = "[${longdate}] [${level:format=FirstCharacter:uppercase=true}] [${logger:shortName=true}] ${message} ${exception:format=ToString,StackTrace}";

                var nullTarget = new NullTarget("null");

                loggingConfig.AddTarget(nullTarget);

                // Suppress some Aspnet stuff
                loggingConfig.AddRule(level, NLog.LogLevel.Info, nullTarget, "Microsoft.AspNetCore.Mvc.Internal.*", true);
                loggingConfig.AddRule(level, NLog.LogLevel.Info, nullTarget, "Microsoft.AspNetCore.Mvc.Infrastructure.*", true);

                // Api Log
                if(!string.IsNullOrEmpty(config.ApiLogFile) && !isShareRecoveryMode)
                {
                    var target = new FileTarget("file")
                    {
                        FileName = GetLogPath(config, config.ApiLogFile),
                        FileNameKind = FilePathKind.Unknown,
                        Layout = layout
                    };

                    loggingConfig.AddTarget(target);
                    loggingConfig.AddRule(level, NLog.LogLevel.Fatal, target, "Microsoft.AspNetCore.*", true);
                }

                if(config.EnableConsoleLog || isShareRecoveryMode)
                {
                    if(config.EnableConsoleColors)
                    {
                        var target = new ColoredConsoleTarget("console")
                        {
                            Layout = layout
                        };

                        target.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(
                            ConditionParser.ParseExpression("level == LogLevel.Trace"),
                            ConsoleOutputColor.DarkMagenta, ConsoleOutputColor.NoChange));

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
                        loggingConfig.AddRule(level, NLog.LogLevel.Fatal, target);
                    }

                    else
                    {
                        var target = new ConsoleTarget("console")
                        {
                            Layout = layout
                        };

                        loggingConfig.AddTarget(target);
                        loggingConfig.AddRule(level, NLog.LogLevel.Fatal, target);
                    }
                }

                if(!string.IsNullOrEmpty(config.LogFile) && !isShareRecoveryMode)
                {
                    var target = new FileTarget("file")
                    {
                        FileName = GetLogPath(config, config.LogFile),
                        FileNameKind = FilePathKind.Unknown,
                        Layout = layout
                    };

                    loggingConfig.AddTarget(target);
                    loggingConfig.AddRule(level, NLog.LogLevel.Fatal, target);
                }

                if(config.PerPoolLogFile && !isShareRecoveryMode)
                {
                    foreach(var poolConfig in clusterConfig.Pools)
                    {
                        var target = new FileTarget(poolConfig.Id)
                        {
                            FileName = GetLogPath(config, poolConfig.Id + ".log"),
                            FileNameKind = FilePathKind.Unknown,
                            Layout = layout
                        };

                        loggingConfig.AddTarget(target);
                        loggingConfig.AddRule(level, NLog.LogLevel.Fatal, target, poolConfig.Id);
                    }
                }
            }

            LogManager.Configuration = loggingConfig;

            logger = LogManager.GetLogger("Core");
        }

        private static Layout GetLogPath(ClusterLoggingConfig config, string name)
        {
            if(string.IsNullOrEmpty(config.LogBaseDirectory))
                return name;

            return Path.Combine(config.LogBaseDirectory, name);
        }

        private static void ConfigureMisc()
        {
            ZcashNetworks.Instance.EnsureRegistered();

            // Configure Equihash
            if(clusterConfig.EquihashMaxThreads.HasValue)
                EquihashSolver.MaxThreads = clusterConfig.EquihashMaxThreads.Value;
        }

        private static void ConfigurePersistence(ContainerBuilder builder)
        {
            if(clusterConfig.Persistence == null &&
                clusterConfig.PaymentProcessing?.Enabled == true &&
                clusterConfig.ShareRelay == null)
                logger.ThrowLogPoolStartupException("Persistence is not configured!");

            if(clusterConfig.Persistence?.Postgres != null)
                ConfigurePostgres(clusterConfig.Persistence.Postgres, builder);
            else
                ConfigureDummyPersistence(builder);
        }

        private static void ConfigurePostgres(DatabaseConfig pgConfig, ContainerBuilder builder)
        {
            // validate config
            if(string.IsNullOrEmpty(pgConfig.Host))
                logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'host'");

            if(pgConfig.Port == 0)
                logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'port'");

            if(string.IsNullOrEmpty(pgConfig.Database))
                logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'database'");

            if(string.IsNullOrEmpty(pgConfig.User))
                logger.ThrowLogPoolStartupException("Postgres configuration: invalid or missing 'user'");

            // build connection string
            var connectionString = $"Server={pgConfig.Host};Port={pgConfig.Port};Database={pgConfig.Database};User Id={pgConfig.User};Password={pgConfig.Password};CommandTimeout=900;";

            // register connection factory
            builder.RegisterInstance(new PgConnectionFactory(connectionString))
                .AsImplementedInterfaces();

            // register repositories
            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t =>
                    t.Namespace.StartsWith(typeof(ShareRepository).Namespace))
                .AsImplementedInterfaces()
                .SingleInstance();
        }

        private static void ConfigureDummyPersistence(ContainerBuilder builder)
        {
            // register connection factory
            builder.RegisterInstance(new DummyConnectionFactory(string.Empty))
                .AsImplementedInterfaces();

            // register repositories
            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t =>
                    t.Namespace.StartsWith(typeof(ShareRepository).Namespace))
                .AsImplementedInterfaces()
                .SingleInstance();
        }

        private Dictionary<string, CoinTemplate> LoadCoinTemplates()
        {
            var basePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var defaultTemplates = Path.Combine(basePath, "coins.json");

            // make sure default templates are loaded first
            clusterConfig.CoinTemplates = new[]
            {
                defaultTemplates
            }
            .Concat(clusterConfig.CoinTemplates != null ?
                clusterConfig.CoinTemplates.Where(x => x != defaultTemplates) :
                new string[0])
            .ToArray();

            return CoinTemplateLoader.Load(container, clusterConfig.CoinTemplates);
        }

        private static void UseIpWhiteList(IApplicationBuilder app, bool defaultToLoopback, string[] locations, string[] whitelist)
        {
            var ipList = whitelist?.Select(IPAddress.Parse).ToList();
            if(defaultToLoopback && (ipList == null || ipList.Count == 0))
                ipList = new List<IPAddress>(new[] { IPAddress.Loopback, IPAddress.IPv6Loopback, IPUtils.IPv4LoopBackOnIPv6 });

            if(ipList.Count > 0)
            {
                // always allow access by localhost
                if(!ipList.Any(x => x.Equals(IPAddress.Loopback)))
                    ipList.Add(IPAddress.Loopback);
                if(!ipList.Any(x => x.Equals(IPAddress.IPv6Loopback)))
                    ipList.Add(IPAddress.IPv6Loopback);
                if(!ipList.Any(x => x.Equals(IPUtils.IPv4LoopBackOnIPv6)))
                    ipList.Add(IPUtils.IPv4LoopBackOnIPv6);

                logger.Info(() => $"API Access to {string.Join(",", locations)} restricted to {string.Join(",", ipList.Select(x => x.ToString()))}");

                app.UseMiddleware<IPAccessWhitelistMiddleware>(locations, ipList.ToArray());
            }
        }

        private static void ConfigureIpRateLimitOptions(IpRateLimitOptions options)
        {
            options.EnableEndpointRateLimiting = false;

            // exclude admin api and metrics from throtteling
            options.EndpointWhitelist = new List<string>
            {
                "*:/api/admin",
                "get:/metrics",
                "*:/notifications",
            };

            options.IpWhitelist = clusterConfig.Api?.RateLimiting?.IpWhitelist?.ToList();

            // default to whitelist localhost if whitelist absent
            if(options.IpWhitelist == null || options.IpWhitelist.Count == 0)
            {
                options.IpWhitelist = new List<string>
                {
                    IPAddress.Loopback.ToString(),
                    IPAddress.IPv6Loopback.ToString(),
                    IPUtils.IPv4LoopBackOnIPv6.ToString()
                };
            }

            // limits
            var rules = clusterConfig.Api?.RateLimiting?.Rules?.ToList();

            if(rules == null || rules.Count == 0)
            {
                rules = new List<RateLimitRule>
                {
                    new()
                    {
                        Endpoint = "*",
                        Period = "1s",
                        Limit = 5,
                    }
                };
            }

            options.GeneralRules = rules;

            logger.Info(() => $"API access limited to {(string.Join(", ", rules.Select(x => $"{x.Limit} requests per {x.Period}")))}, except from {string.Join(", ", options.IpWhitelist)}");
        }

        private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if(logger != null)
            {
                logger.Error(e.ExceptionObject);
                LogManager.Flush(TimeSpan.Zero);
            }

            Console.WriteLine("** AppDomain unhandled exception: {0}", e.ExceptionObject);
        }
    }
}
