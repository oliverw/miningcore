using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using AspNetCoreRateLimit;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Features.Metadata;
using AutoMapper;
using Dapper;
using FluentValidation;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Miningcore.Api;
using Miningcore.Api.Controllers;
using Miningcore.Api.Middlewares;
using Miningcore.Api.Responses;
using Miningcore.Configuration;
using Miningcore.Crypto.Hashing.Algorithms;
using Miningcore.Crypto.Hashing.Equihash;
using Miningcore.Crypto.Hashing.Ethash;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Native;
using Miningcore.Notifications;
using Miningcore.Payments;
using Miningcore.Persistence;
using Miningcore.Persistence.Dummy;
using Miningcore.Persistence.Postgres;
using Miningcore.Persistence.Postgres.Repositories;
using Miningcore.Util;
using NBitcoin.Zcash;
using Newtonsoft.Json;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Schema.Generation;
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
using static Miningcore.Util.ActionUtils;

// ReSharper disable AssignNullToNotNullAttribute
// ReSharper disable PossibleNullReferenceException

[assembly: InternalsVisibleToAttribute("Miningcore.Tests")]

namespace Miningcore;

public class Program : BackgroundService
{
    public static async Task Main(string[] args)
    {
        try
        {
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

            var app = ParseCommandLine(args);

            if(versionOption.HasValue())
            {
                app.ShowVersion();
                return;
            }

            if(dumpConfigOption.HasValue())
            {
                DumpParsedConfig(clusterConfig);
                return;
            }

            if(generateSchemaOption.HasValue())
            {
                GenerateJsonConfigSchema();
                return;
            }

            if(!configFileOption.HasValue())
            {
                app.ShowHelp();
                return;
            }

            Logo();

            isShareRecoveryMode = shareRecoveryOption.HasValue();
            clusterConfig = ReadConfig(configFileOption.Value());

            ValidateConfig();

            ConfigureLogging();
            LogRuntimeInfo();
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
                    services.AddHttpClient();
                    services.AddMemoryCache();

                    ConfigureBackgroundServices(services);

                    // MUST BE THE LAST REGISTERED HOSTED SERVICE!
                    services.AddHostedService<Program>();
                });

            if(clusterConfig.Api == null || clusterConfig.Api.Enabled)
            {
                var address = clusterConfig.Api?.ListenAddress != null
                    ? (clusterConfig.Api.ListenAddress != "*" ? IPAddress.Parse(clusterConfig.Api.ListenAddress) : IPAddress.Any)
                    : IPAddress.Parse("127.0.0.1");

                var port = clusterConfig.Api?.Port ?? 4000;
                var enableApiRateLimiting = clusterConfig.Api?.RateLimiting?.Disabled != true;
                var apiTlsEnable = clusterConfig.Api?.Tls?.Enabled == true || !string.IsNullOrEmpty(clusterConfig.Api?.Tls?.TlsPfxFile);

                if(apiTlsEnable)
                {
                    if(!File.Exists(clusterConfig.Api.Tls.TlsPfxFile))
                        throw new PoolStartupException($"Certificate file {clusterConfig.Api.Tls.TlsPfxFile} does not exist!");
                }

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
                            services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
                        }

                        // Controllers
                        services.AddSingleton<PoolApiController, PoolApiController>();
                        services.AddSingleton<AdminApiController, AdminApiController>();

                        // MVC
                        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

                        services.AddMvc(options =>
                        {
                            options.EnableEndpointRouting = false;
                        })
                        .AddControllersAsServices()
                        .AddJsonOptions(options =>
                        {
                            options.JsonSerializerOptions.WriteIndented = true;

                            if(!clusterConfig.Api.LegacyNullValueHandling)
                                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                        });

                        // NSwag
                        #if DEBUG
                        services.AddOpenApiDocument(settings =>
                        {
                            settings.DocumentProcessors.Insert(0, new NSwagDocumentProcessor());
                        });
                        #endif

                        services.AddResponseCompression();
                        services.AddCors();
                        services.AddWebSocketManager();
                    })
                    .UseKestrel(options =>
                    {
                        options.Listen(address, port, listenOptions =>
                        {
                            if(apiTlsEnable)
                                listenOptions.UseHttps(clusterConfig.Api.Tls.TlsPfxFile, clusterConfig.Api.Tls.TlsPfxPassword);
                        });
                    })
                    .Configure(app =>
                    {
                        if(enableApiRateLimiting)
                            app.UseIpRateLimiting();

                        app.UseMiddleware<ApiExceptionHandlingMiddleware>();

                        UseIpWhiteList(app, true, new[]
                        {
                            "/api/admin"
                        }, clusterConfig.Api?.AdminIpWhitelist);
                        UseIpWhiteList(app, true, new[]
                        {
                            "/metrics"
                        }, clusterConfig.Api?.MetricsIpWhitelist);

                        #if DEBUG
                        app.UseOpenApi();
                        #endif

                        app.UseResponseCompression();
                        app.UseCors(corsPolicyBuilder => corsPolicyBuilder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
                        app.UseWebSockets();
                        app.MapWebSocketManager("/notifications", app.ApplicationServices.GetService<WebSocketNotificationsRelay>());
                        app.UseMetricServer();

                        app.UseMiddleware<ApiRequestMetricsMiddleware>();

                        app.UseMvc();
                    });

                    logger.Info(() => $"Prometheus Metrics API listening on http{(apiTlsEnable ? "s" : "")}://{address}:{port}/metrics");
                    logger.Info(() => $"WebSocket Events streaming on ws{(apiTlsEnable ? "s" : "")}://{address}:{port}/notifications");
                });
            }

            host = hostBuilder.UseConsoleLifetime().Build();

            await PreFlightChecks(host.Services);

            await host.RunAsync();
        }

        catch(PoolStartupException ex)
        {
            if(!string.IsNullOrEmpty(ex.Message))
                await Console.Error.WriteLineAsync(ex.Message);

            await Console.Error.WriteLineAsync("\nCluster cannot start. Good Bye!");
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
            if(ex.InnerExceptions.First() is not PoolStartupException)
                Console.Error.WriteLine(ex);

            await Console.Error.WriteLineAsync("Cluster cannot start. Good Bye!");
        }

        catch(OperationCanceledException)
        {
            // Ctrl+C
        }

        catch(Exception ex)
        {
            Console.Error.WriteLine(ex);

            await Console.Error.WriteLineAsync("Cluster cannot start. Good Bye!");
        }
    }

    private static void ConfigureBackgroundServices(IServiceCollection services)
    {
        services.AddHostedService<NotificationService>();
        services.AddHostedService<BtStreamReceiver>();

        // Share processing
        if(clusterConfig.ShareRelay == null)
        {
            services.AddHostedService<ShareRecorder>();
            services.AddHostedService<ShareReceiver>();
        }

        else
            services.AddHostedService<ShareRelay>();

        // API
        if(clusterConfig.Api == null || clusterConfig.Api.Enabled)
            services.AddHostedService<MetricsPublisher>();

        // Payment processing
        if(clusterConfig.PaymentProcessing?.Enabled == true &&
           clusterConfig.Pools.Any(x => x.PaymentProcessing?.Enabled == true))
            services.AddHostedService<PayoutManager>();
        else
            logger.Info("Payment processing is not enabled");

        if(clusterConfig.ShareRelay == null)
        {
            // Pool stats
            services.AddHostedService<StatsRecorder>();
        }
    }

    private static IHost host;
    private readonly IComponentContext container;
    private readonly IHostApplicationLifetime hal;
    private static ILogger logger;
    private static CommandOption versionOption;
    private static CommandOption configFileOption;
    private static CommandOption dumpConfigOption;
    private static CommandOption shareRecoveryOption;
    private static CommandOption generateSchemaOption;
    private static bool isShareRecoveryMode;
    private static ClusterConfig clusterConfig;
    private static readonly ConcurrentDictionary<string, IMiningPool> pools = new();
    private static readonly AdminGcStats gcStats = new();

    public Program(IComponentContext container, IHostApplicationLifetime hal)
    {
        this.container = container;
        this.hal = hal;
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
        if(isShareRecoveryMode)
        {
            await RecoverSharesAsync(shareRecoveryOption.Value());
            return;
        }

        if(clusterConfig.InstanceId.HasValue)
            logger.Info($"This is cluster node {clusterConfig.InstanceId.Value}{(!string.IsNullOrEmpty(clusterConfig.ClusterName) ? $" [{clusterConfig.ClusterName}]" : string.Empty)}");

        var coinTemplates = LoadCoinTemplates();
        logger.Info($"{coinTemplates.Keys.Count} coins loaded from '{string.Join(", ", clusterConfig.CoinTemplates)}'");

        var tasks = clusterConfig.Pools
            .Where(config => config.Enabled)
            .Select(config => RunPool(config, coinTemplates, ct));

        await Guard(()=> Task.WhenAll(tasks), ex =>
        {
            switch(ex)
            {
                case PoolStartupException pse:
                {
                    var _logger = pse.PoolId != null ? LogUtil.GetPoolScopedLogger(GetType(), pse.PoolId) : logger;
                    _logger.Error(() => $"{pse.Message}");

                    logger.Error(() => "Cluster cannot start. Good Bye!");

                    hal.StopApplication();
                    break;
                }

                default:
                    throw ex;
            }
        });
    }

    private async Task RunPool(PoolConfig poolConfig, Dictionary<string, CoinTemplate> coinTemplates, CancellationToken ct)
    {
        // Lookup coin
        if(!coinTemplates.TryGetValue(poolConfig.Coin, out var template))
            throw new PoolStartupException($"Pool {poolConfig.Id} references undefined coin '{poolConfig.Coin}'", poolConfig.Id);

        poolConfig.Template = template;

        // resolve implementation
        var poolImpl = container.Resolve<IEnumerable<Meta<Lazy<IMiningPool, CoinFamilyAttribute>>>>()
            .First(x => x.Value.Metadata.SupportedFamilies.Contains(poolConfig.Template.Family)).Value;

        // configure
        var pool = poolImpl.Value;
        pool.Configure(poolConfig, clusterConfig);
        pools[poolConfig.Id] = pool;

        // go
        await pool.RunAsync(ct);
    }

    private Task RecoverSharesAsync(string recoveryFilename)
    {
        var shareRecorder = container.Resolve<ShareRecorder>();
        return shareRecorder.RecoverSharesAsync(recoveryFilename);
    }

    private static void LogRuntimeInfo()
    {
        logger.Info(() => $"Version {GetVersion()}");

        logger.Info(() => $"Runtime {RuntimeInformation.FrameworkDescription.Trim()} on {RuntimeInformation.OSDescription.Trim()} [{RuntimeInformation.ProcessArchitecture}]");
    }

    private static string GetVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var gitVersionInformationType = assembly.GetType("GitVersionInformation");

        if(gitVersionInformationType != null)
        {
            var assemblySemVer = gitVersionInformationType.GetField("AssemblySemVer").GetValue(null);
            var branchName = gitVersionInformationType.GetField("BranchName").GetValue(null);
            var sha = gitVersionInformationType.GetField("Sha").GetValue(null);

            return $"{assemblySemVer}-{branchName} [{sha}]";
        }

        else
            return "unknown";
    }

    private static void ValidateConfig()
    {
        if(!clusterConfig.Pools.Any(x => x.Enabled))
            throw new PoolStartupException("No pools are enabled.");

        // set some defaults
        foreach(var config in clusterConfig.Pools)
        {
            config.EnableInternalStratum ??= clusterConfig.ShareRelays == null || clusterConfig.ShareRelays.Length == 0;
        }

        try
        {
            clusterConfig.Validate();

            if(clusterConfig.Notifications?.Admin?.Enabled == true)
            {
                if(string.IsNullOrEmpty(clusterConfig.Notifications?.Email?.FromName))
                    throw new PoolStartupException($"Notifications are enabled but email sender name is not configured (notifications.email.fromName)");

                if(string.IsNullOrEmpty(clusterConfig.Notifications?.Email?.FromAddress))
                    throw new PoolStartupException($"Notifications are enabled but email sender address name is not configured (notifications.email.fromAddress)");

                if(string.IsNullOrEmpty(clusterConfig.Notifications?.Admin?.EmailAddress))
                    throw new PoolStartupException($"Admin notifications are enabled but recipient address is not configured (notifications.admin.emailAddress)");
            }

            if(string.IsNullOrEmpty(clusterConfig.Logging.LogFile))
            {
                // emit a newline before regular logging output starts
                Console.WriteLine();
            }
        }

        catch(ValidationException ex)
        {
            Console.Error.WriteLine($"Configuration is not valid:\n\n{string.Join("\n", ex.Errors.Select(x => "=> " + x.ErrorMessage))}");
            throw new PoolStartupException(string.Empty);
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

    private static void GenerateJsonConfigSchema()
    {
        var filename = generateSchemaOption.Value();

        var generator = new JSchemaGenerator
        {
            DefaultRequired = Required.Default,
            SchemaPropertyOrderHandling = SchemaPropertyOrderHandling.Alphabetical,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            GenerationProviders =
            {
                new StringEnumGenerationProvider()
            }
        };

        var schema = generator.Generate(typeof(ClusterConfig));

        using(var stream = File.Create(filename))
        {
            using(var writer = new JsonTextWriter(new StreamWriter(stream, Encoding.UTF8)))
            {
                schema.WriteTo(writer);

                writer.Flush();
            }
        }
    }

    private static CommandLineApplication ParseCommandLine(string[] args)
    {
        var app = new CommandLineApplication
        {
            FullName = "Miningcore",
            ShortVersionGetter = GetVersion,
            LongVersionGetter = GetVersion
        };

        versionOption = app.Option("-v|--version", "Version Information", CommandOptionType.NoValue);
        configFileOption = app.Option("-c|--config <configfile>", "Configuration File", CommandOptionType.SingleValue);
        dumpConfigOption = app.Option("-dc|--dumpconfig", "Dump the configuration (useful for trouble-shooting typos in the config file)",CommandOptionType.NoValue);
        shareRecoveryOption = app.Option("-rs", "Import lost shares using existing recovery file", CommandOptionType.SingleValue);
        generateSchemaOption = app.Option("-gcs|--generate-config-schema <outputfile>", "Generate JSON schema from configuration options", CommandOptionType.SingleValue);
        app.HelpOption("-? | -h | --help");

        app.Execute(args);

        return app;
    }

    private static ClusterConfig ReadConfig(string file)
    {
        try
        {
            Console.WriteLine($"Using configuration file '{file}'");

            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });

            using(var reader = new StreamReader(file, Encoding.UTF8))
            {
                using(var jsonReader = new JsonTextReader(reader))
                {
                    using(var validatingReader = new JSchemaValidatingReader(jsonReader)
                    {
                        Schema =  LoadSchema()
                    })
                    {
                        return serializer.Deserialize<ClusterConfig>(validatingReader);
                    }
                }
            }
        }

        catch(JSchemaValidationException ex)
        {
            throw new PoolStartupException($"Configuration file error: {ex.Message}");
        }

        catch(JsonSerializationException ex)
        {
            throw new PoolStartupException($"Configuration file error: {ex.Message}");
        }

        catch(JsonException ex)
        {
            throw new PoolStartupException($"Configuration file error: {ex.Message}");
        }

        catch(IOException ex)
        {
            throw new PoolStartupException($"Configuration file error: {ex.Message}");
        }
    }

    private static JSchema LoadSchema()
    {
        var basePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        var path = Path.Combine(basePath, "config.schema.json");

        using(var reader = new JsonTextReader(new StreamReader(File.OpenRead(path))))
        {
            return JSchema.Load(reader);
        }
    }

    private static void ValidateRuntimeEnvironment()
    {
        // root check
        if(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Environment.UserName == "root")
            logger.Warn(() => "Running as root is discouraged!");

        // require 64-bit on Windows
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.ProcessArchitecture == Architecture.X86)
            throw new PoolStartupException("Miningcore requires 64-Bit Windows");
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
        Console.WriteLine(" https://github.com/oliverw/miningcore\n");
        Console.WriteLine(" Donate to one of these addresses to support the project:\n");
        Console.WriteLine(" ETH  - miningcore.eth (ENS Address)");
        Console.WriteLine(" BTC  - miningcore.eth (ENS Address)");
        Console.WriteLine(" LTC  - miningcore.eth (ENS Address)");
        Console.WriteLine(" DASH - XqpBAV9QCaoLnz42uF5frSSfrJTrqHoxjp");
        Console.WriteLine(" ZEC  - t1YHZHz2DGVMJiggD2P4fBQ2TAPgtLSUwZ7");
        Console.WriteLine(" ZCL  - t1MFU1vD3YKgsK6Uh8hW7UTY8mKAV2xVqBr");
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

            // Suppress some log spam
            loggingConfig.AddRule(level, NLog.LogLevel.Info, nullTarget, "Microsoft.AspNetCore.Mvc.Internal.*", true);
            loggingConfig.AddRule(level, NLog.LogLevel.Info, nullTarget, "Microsoft.AspNetCore.Mvc.Infrastructure.*", true);
            loggingConfig.AddRule(level, NLog.LogLevel.Warn, nullTarget, "System.Net.Http.HttpClient.*", true);
            loggingConfig.AddRule(level, NLog.LogLevel.Fatal, nullTarget, "Microsoft.Extensions.Hosting.Internal.*", true);

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

    private static async Task PreFlightChecks(IServiceProvider services)
    {
        await ConfigurePostgresCompatibilityOptions(services);

        ZcashNetworks.Instance.EnsureRegistered();

        var messageBus = services.GetService<IMessageBus>();
        var rmsm = services.GetService<RecyclableMemoryStreamManager>();

        // Configure RecyclableMemoryStream
        rmsm.MaximumFreeSmallPoolBytes = clusterConfig.Memory?.RmsmMaximumFreeSmallPoolBytes ?? 0x100000;   // 1 MB
        rmsm.MaximumFreeLargePoolBytes = clusterConfig.Memory?.RmsmMaximumFreeLargePoolBytes ?? 0x800000;   // 8 MB

        // Configure Equihash
        EquihashSolver.messageBus = messageBus;
        EquihashSolver.MaxThreads = clusterConfig.EquihashMaxThreads ?? 1;

        // Configure Ethhash
        Dag.messageBus = messageBus;

        // Configure Verthash
        Verthash.messageBus = messageBus;

        // Configure Cryptonight
        Cryptonight.messageBus = messageBus;
        Cryptonight.InitContexts(GetDefaultConcurrency(clusterConfig.CryptonightMaxThreads));

        // Configure RandomX
        RandomX.messageBus = messageBus;

        // Configure RandomARQ
        RandomARQ.messageBus = messageBus;
    }

    private static async Task ConfigurePostgresCompatibilityOptions(IServiceProvider services)
    {
        if(clusterConfig.Persistence?.Postgres == null)
            return;

        var cf = services.GetService<IConnectionFactory>();

        bool enableLegacyTimestampBehavior = false;

        if(!clusterConfig.Persistence.Postgres.EnableLegacyTimestamps.HasValue)
        {
            // check if 'shares.created' is legacy timestamp (without timezone)
            var columnType = await GetPostgresColumnType(cf, "shares", "created");

            if(columnType != null)
                enableLegacyTimestampBehavior = columnType.ToLower().Contains("without time zone");
            else
                logger.Warn(() => "Unable to auto-detect Npgsql Legacy Timestamp Behavior. Please set 'EnableLegacyTimestamps' in your Miningcore Database configuration to'true' or 'false' to bypass auto-detection in case of problems");
        }

        else
            enableLegacyTimestampBehavior = clusterConfig.Persistence.Postgres.EnableLegacyTimestamps.Value;

        if(enableLegacyTimestampBehavior)
        {
            logger.Info(()=> "Enabling Npgsql Legacy Timestamp Behavior");

            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }
    }

    private static async Task<string> GetPostgresColumnType(IConnectionFactory cf, string table, string column)
    {
        const string query = "SELECT data_type FROM information_schema.columns WHERE table_name = @table AND column_name = @column";

        return await cf.Run(async con => await con.ExecuteScalarAsync<string>(query, new { table, column }));
    }

    private static void ConfigurePersistence(ContainerBuilder builder)
    {
        if(clusterConfig.Persistence == null &&
           clusterConfig.PaymentProcessing?.Enabled == true &&
           clusterConfig.ShareRelay == null)
            throw new PoolStartupException("Persistence is not configured!");

        if(clusterConfig.Persistence?.Postgres != null)
            ConfigurePostgres(clusterConfig.Persistence.Postgres, builder);
        else
            ConfigureDummyPersistence(builder);
    }

    private static void ConfigurePostgres(PostgresConfig pgConfig, ContainerBuilder builder)
    {
        // validate config
        if(string.IsNullOrEmpty(pgConfig.Host))
            throw new PoolStartupException("Postgres configuration: invalid or missing 'host'");

        if(pgConfig.Port == 0)
            throw new PoolStartupException("Postgres configuration: invalid or missing 'port'");

        if(string.IsNullOrEmpty(pgConfig.Database))
            throw new PoolStartupException("Postgres configuration: invalid or missing 'database'");

        if(string.IsNullOrEmpty(pgConfig.User))
            throw new PoolStartupException("Postgres configuration: invalid or missing 'user'");

        // build connection string
        var connectionString = new StringBuilder($"Server={pgConfig.Host};Port={pgConfig.Port};Database={pgConfig.Database};User Id={pgConfig.User};Password={pgConfig.Password};");

        if(pgConfig.Tls)
        {
            connectionString.Append("SSL Mode=Require;");

            if(pgConfig.TlsNoValidate)
                connectionString.Append("Trust Server Certificate=true;");

            if(!string.IsNullOrEmpty(pgConfig.TlsCert?.Trim()))
                connectionString.Append($"SSL Certificate={pgConfig.TlsCert.Trim()};");

            if(!string.IsNullOrEmpty(pgConfig.TlsKey?.Trim()))
                connectionString.Append($"SSL Key={pgConfig.TlsKey.Trim()};");

            if(!string.IsNullOrEmpty(pgConfig.TlsPassword))
                connectionString.Append($"SSL Password={pgConfig.TlsPassword};");
        }

        connectionString.Append($"CommandTimeout={pgConfig.CommandTimeout ?? 300};");

        logger.Debug(()=> $"Using postgres connection string: {connectionString}");

        // register connection factory
        builder.RegisterInstance(new PgConnectionFactory(connectionString.ToString()))
            .AsImplementedInterfaces();

        // register repositories
        builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
            .Where(t =>
                t?.Namespace?.StartsWith(typeof(ShareRepository).Namespace) == true)
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
            .Where(t => t?.Namespace?.StartsWith(typeof(ShareRepository).Namespace) == true)
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
        .Concat(clusterConfig.CoinTemplates != null ? clusterConfig.CoinTemplates.Where(x => x != defaultTemplates) : Array.Empty<string>())
        .ToArray();

        return CoinTemplateLoader.Load(container, clusterConfig.CoinTemplates);
    }

    private static void UseIpWhiteList(IApplicationBuilder app, bool defaultToLoopback, string[] locations, string[] whitelist)
    {
        var ipList = whitelist?.Select(IPAddress.Parse).ToList();
        if(defaultToLoopback && (ipList == null || ipList.Count == 0))
            ipList = new List<IPAddress>(new[]
            {
                IPAddress.Loopback, IPAddress.IPv6Loopback, IPUtils.IPv4LoopBackOnIPv6
            });

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

            app.UseMiddleware<IPAccessWhitelistMiddleware>(locations, ipList.ToArray(), clusterConfig.Logging.GPDRCompliant);
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

    private static int GetDefaultConcurrency(int? value)
    {
        value = value switch
        {
            null => 1,
            -1 => Environment.ProcessorCount,
            _ => value
        };

        return value.Value;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if(logger != null)
        {
            logger.Error(e.ExceptionObject);
            LogManager.Flush(TimeSpan.Zero);
        }

        Console.Error.WriteLine("** AppDomain unhandled exception: {0}", e.ExceptionObject);
    }
}
