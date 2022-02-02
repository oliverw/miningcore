using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AspNetCoreRateLimit;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Features.Metadata;
using AutoMapper;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Dapper;
using FluentValidation;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
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
using Miningcore.Persistence.Cosmos.Repositories;
using Miningcore.Persistence.Dummy;
using Miningcore.Persistence.Postgres;
using Miningcore.Persistence.Postgres.Repositories;
using Miningcore.Util;
using NBitcoin.Zcash;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

            isShareRecoveryMode = shareRecoveryOption.HasValue();

            var envConfig = Environment.GetEnvironmentVariable(EnvironmentConfig);
            if(configFileOption.HasValue())
            {
                clusterConfig = ReadConfig(configFileOption.Value());
            }
            else if(appConfigPrefixOption.HasValue())
            {
                var vault = Environment.GetEnvironmentVariable(VaultName);
                if(!string.IsNullOrEmpty(vault))
                {
                    clusterConfig = ReadConfigFromKeyVault(vault, appConfigPrefixOption.Value());
                }
                else
                {
                    clusterConfig = ReadConfigFromAppConfig(Environment.GetEnvironmentVariable(AppConfigConnectionStringEnvVar), appConfigPrefixOption.Value());
                }
            }
            else if(!string.IsNullOrEmpty(envConfig))
            {
                clusterConfig = ReadConfigFromJson(envConfig);
            }
            else
            {
                app.ShowHelp();
                return;
            }

            Logo();

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

                        // Prevent null pointer when launching from a test container
                        services.AddTransient<WebSocketNotificationsRelay>();

                        // NSwag
#if DEBUG
                        services.AddOpenApiDocument(settings =>
                        {
                            settings.DocumentProcessors.Insert(0, new NSwagDocumentProcessor());
                        });
#endif

                        // Authentication
                        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                            .AddJwtBearer(options =>
                            {
                                options.Authority = clusterConfig.Api.OidcValidIssuer;
                                options.Audience = clusterConfig.Api.OidcValidateAudience ? clusterConfig.Api.OidcValidAudience : null;
                                options.MetadataAddress = clusterConfig.Api.OidcMetadataAddress;
                                options.RequireHttpsMetadata = true;
                                options.TokenValidationParameters = new TokenValidationParameters
                                {
                                    ValidateIssuer = true,
                                    ValidateLifetime = true,
                                    ValidateIssuerSigningKey = true,
                                    ValidateAudience = clusterConfig.Api.OidcValidateAudience,
                                };
                            });

                        // Gzip compression
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
                        app.UseMvc();
                        app.UseAuthentication();
                        app.UseAuthorization();
                    });

                    logger.Info(() => $"Prometheus Metrics {address}:{port}/metrics");
                    logger.Info(() => $"WebSocket notifications streaming {address}:{port}/notifications");
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

    private const string AppConfigConnectionStringEnvVar = "ConnectionString";
    private const string BaseConfigFile = "config.json";
    private const string CoinbasePassword = "coinbasePassword";
    private const string EnvironmentConfig = "cfg";
    private const string PrivateKey = "privateKey";
    private const string VaultName = "akv";
    private static readonly Regex RegexJsonTypeConversionError = new Regex("\"([^\"]+)\"[^\']+\'([^\']+)\'.+\\s(\\d+),.+\\s(\\d+)", RegexOptions.Compiled);

    private static IHost host;
    private readonly IComponentContext container;
    private static ILogger logger;
    private static CommandOption versionOption;
    private static CommandOption configFileOption;
    private static CommandOption appConfigPrefixOption;
    private static CommandOption dumpConfigOption;
    private static CommandOption shareRecoveryOption;
    private static CommandOption generateSchemaOption;
    private static bool isShareRecoveryMode;
    private static ClusterConfig clusterConfig;
    private static IConfigurationRoot remoteConfig;
    private static readonly ConcurrentDictionary<string, IMiningPool> pools = new();
    private static readonly AdminGcStats gcStats = new();

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
        if(isShareRecoveryMode)
        {
            await RecoverSharesAsync(shareRecoveryOption.Value());
            return;
        }

        if(clusterConfig.InstanceId.HasValue)
            logger.Info($"This is cluster node {clusterConfig.InstanceId.Value}{(!string.IsNullOrEmpty(clusterConfig.ClusterName) ? $" [{clusterConfig.ClusterName}]" : string.Empty)}");

        var coinTemplates = LoadCoinTemplates();
        logger.Info($"{coinTemplates.Keys.Count} coins loaded from '{string.Join(", ", clusterConfig.CoinTemplates)}'");

        await Task.WhenAll(clusterConfig.Pools
            .Where(config => config.Enabled)
            .Select(config => RunPool(config, coinTemplates, ct)));
    }

    private Task RunPool(PoolConfig poolConfig, Dictionary<string, CoinTemplate> coinTemplates, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            // Lookup coin
            if(!coinTemplates.TryGetValue(poolConfig.Coin, out var template))
                throw new PoolStartupException($"Pool {poolConfig.Id} references undefined coin '{poolConfig.Coin}'");

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
        }, ct);
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
        appConfigPrefixOption = app.Option("-ac|--appconfig <prefix>", "Azure App Configuration Prefix", CommandOptionType.SingleValue);
        dumpConfigOption = app.Option("-dc|--dumpconfig", "Dump the configuration (useful for trouble-shooting typos in the config file)", CommandOptionType.NoValue);
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
                        Schema = LoadSchema()
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

            // Suppress some log spam
            loggingConfig.AddRule(level, NLog.LogLevel.Info, nullTarget, "Microsoft.AspNetCore.Mvc.Internal.*", true);
            loggingConfig.AddRule(level, NLog.LogLevel.Info, nullTarget, "Microsoft.AspNetCore.Mvc.Infrastructure.*", true);
            loggingConfig.AddRule(level, NLog.LogLevel.Warn, nullTarget, "System.Net.Http.HttpClient.*", true);

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

        // Configure Equihash
        EquihashSolver.messageBus = messageBus;
        EquihashSolver.MaxThreads = clusterConfig.EquihashMaxThreads ?? 1;

        // Configure Ethhash
        Dag.messageBus = messageBus;

        // Configure Verthash
        Verthash.messageBus = messageBus;

        // Configure Cryptonight
        Cryptonight.messageBus = messageBus;
        Cryptonight.InitContexts(clusterConfig.CryptonightMaxThreads ?? 1);

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

        // check if 'shares.created' is legacy timestamp (without timezone)
        var columnType = await GetPostgresColumnType(cf, "shares", "created");
        var isLegacyTimestamps = columnType.ToLower().Contains("without time zone");

        if(isLegacyTimestamps)
        {
            logger.Info(() => "Enabling Npgsql Legacy Timestamp Behavior");

            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }
    }

    private static Task<string> GetPostgresColumnType(IConnectionFactory cf, string table, string column)
    {
        const string query = "SELECT data_type FROM information_schema.columns WHERE table_name = @table AND column_name = @column";

        return cf.Run(con => con.ExecuteScalarAsync<string>(query, new { table, column }));
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

        if(clusterConfig.Persistence?.Cosmos != null)
        {
            ConfigureCosmos(clusterConfig.Persistence.Cosmos, builder);
        }
    }

    private static void ConfigureCosmos(CosmosConfig cosmosConfig, ContainerBuilder builder)
    {
        // validate config
        if(string.IsNullOrEmpty(cosmosConfig.EndpointUrl))
            throw new PoolStartupException("Cosmos configuration: invalid or missing 'endpoint url'");

        if(string.IsNullOrEmpty(cosmosConfig.AuthorizationKey))
            throw new PoolStartupException("Cosmos configuration: invalid or missing 'authorizationKey'");

        if(string.IsNullOrEmpty(cosmosConfig.DatabaseId))
            throw new PoolStartupException("Comos configuration: invalid or missing 'databaseId'");

        logger.Info(() => $"Connecting to Cosmos Server {cosmosConfig.EndpointUrl}");

        try
        {
            var cosmosClientOptions = new CosmosClientOptions();

            if(Enum.TryParse(cosmosConfig.ConsistencyLevel, out ConsistencyLevel consistencyLevel))
                cosmosClientOptions.ConsistencyLevel = consistencyLevel;

            if(Enum.TryParse(cosmosConfig.ConnectionMode, out ConnectionMode connectionMode))
                cosmosClientOptions.ConnectionMode = connectionMode;

            if(Double.TryParse(cosmosConfig.RequestTimeout, out Double requestTimeout))
                cosmosClientOptions.RequestTimeout = TimeSpan.FromSeconds(requestTimeout);

            if(int.TryParse(cosmosConfig.MaxRetryAttempt, out int maxRetryAttempt))
                cosmosClientOptions.MaxRetryAttemptsOnRateLimitedRequests = maxRetryAttempt;

            if(Double.TryParse(cosmosConfig.MaxRetryWaitTime, out Double maxRetryWaitTime))
                cosmosClientOptions.MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(maxRetryWaitTime);

            if(int.TryParse(cosmosConfig.MaxPoolSize, out int maxPoolSize))
                cosmosClientOptions.MaxRequestsPerTcpConnection = maxPoolSize;

            if(cosmosConfig.PreferredLocations != null && cosmosConfig.PreferredLocations.Count > 0)
                cosmosClientOptions.ApplicationPreferredRegions = cosmosConfig.PreferredLocations;

            var cosmos = new CosmosClient(cosmosConfig.EndpointUrl, cosmosConfig.AuthorizationKey, cosmosClientOptions);

            // register CosmosClient
            builder.RegisterInstance(cosmos).AsSelf().SingleInstance();

            // register repositories
            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.Namespace.StartsWith(typeof(BalanceChangeRepository).Namespace))
                .AsImplementedInterfaces()
                .SingleInstance();
        }
        catch(Exception e)
        {
            throw new PoolStartupException($"Failed to connect to the cosmos database {e}");
        }
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
        var connectionString = new StringBuilder($"Server={pgConfig.Host};Port={pgConfig.Port};Database={pgConfig.Database};User Id={pgConfig.User};Password={pgConfig.Password};Timeout=60;CommandTimeout=60;Keepalive=60;");

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

        if(pgConfig.CommandTimeout.HasValue)
            connectionString.Append($"CommandTimeout={pgConfig.CommandTimeout.Value};");

        if(pgConfig.Pooling != null)
            connectionString.Append($"Pooling=true;Minimum Pool Size={pgConfig.Pooling.MinPoolSize};Maximum Pool Size={(pgConfig.Pooling.MaxPoolSize > 0 ? pgConfig.Pooling.MaxPoolSize : 100)};");

        if(pgConfig.Ssl)
            connectionString.Append("SSL Mode=Require;Trust Server Certificate=True;Server Compatibility Mode=Redshift;");

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

        Console.Error.WriteLine("** AppDomain unhandled exception: {0}", e.ExceptionObject);
    }

    // Remote configuration
    private static ClusterConfig ReadConfigFromJson(string config)
    {
        try
        {
            var baseConfig = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(BaseConfigFile));
            var toBeMerged = JObject.Parse(config);
            baseConfig.Merge(toBeMerged, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Merge });
            clusterConfig = baseConfig.ToObject<ClusterConfig>();
        }
        catch(JsonSerializationException ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
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

        return clusterConfig;
    }

    private static ClusterConfig ReadConfigFromAppConfig(string connectionString, string prefix)
    {
        Console.WriteLine("Loading config from app config");
        if(prefix.Trim().Equals("/")) prefix = string.Empty;
        try
        {
            // Read AppConfig
            var builder = new ConfigurationBuilder();
            builder.AddAzureAppConfiguration(options => options.Connect(connectionString)
                .ConfigureKeyVault(kv => kv.SetCredential(new DefaultAzureCredential())));
            remoteConfig = builder.Build();

            var secretName = prefix + BaseConfigFile;
            ReadConfigFromJson(remoteConfig[secretName]);
            // Update dynamic pass and others config here
            clusterConfig.Persistence.Postgres.User = remoteConfig.TryGetValue(AppConfigConstants.PersistencePostgresUser, clusterConfig.Persistence.Postgres.User);
            clusterConfig.Persistence.Postgres.Password = remoteConfig.TryGetValue(AppConfigConstants.PersistencePostgresPassword, clusterConfig.Persistence.Postgres.Password);
            clusterConfig.Persistence.Cosmos.AuthorizationKey = remoteConfig.TryGetValue(AppConfigConstants.PersistenceCosmosAuthorizationKey, clusterConfig.Persistence.Cosmos.AuthorizationKey);
            foreach(var poolConfig in clusterConfig.Pools)
            {
                poolConfig.PaymentProcessing.Extra[CoinbasePassword] = remoteConfig.TryGetValue(string.Format(AppConfigConstants.CoinBasePassword, poolConfig.Id), poolConfig.PaymentProcessing.Extra[CoinbasePassword]?.ToString());
                poolConfig.PaymentProcessing.Extra[PrivateKey] = remoteConfig.TryGetValue(string.Format(AppConfigConstants.PrivateKey, poolConfig.Id), poolConfig.PaymentProcessing.Extra[PrivateKey]?.ToString());
                poolConfig.EtherScan.ApiKey = remoteConfig.TryGetValue(string.Format(AppConfigConstants.EtherscanApiKey, poolConfig.Id), poolConfig.EtherScan.ApiKey);
                foreach(var p in poolConfig.Ports)
                {
                    try
                    {
                        if(!p.Value.Tls) continue;
                        var cert = remoteConfig[string.Format(AppConfigConstants.TlsPfxFile, poolConfig.Id, p.Key)];
                        if(cert == null) continue;
                        p.Value.TlsPfx = new X509Certificate2(Convert.FromBase64String(cert), (string) null, X509KeyStorageFlags.MachineKeySet);
                        Console.WriteLine("Successfully loaded TLS certificate from app config");
                    }
                    catch(Exception ex)
                    {
                        Console.WriteLine($"Failed to load TLS certificate from app config, Error={ex.Message}");
                    }
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

        return clusterConfig;
    }

    private static ClusterConfig ReadConfigFromKeyVault(string vaultName, string prefix)
    {
        Console.WriteLine($"Loading config from key vault '{vaultName}'");
        if(prefix.Trim().Equals("/")) prefix = string.Empty;

        // Read KeyVault
        var builder = new ConfigurationBuilder();
        builder.AddAzureKeyVault(new SecretClient(new Uri($"https://{vaultName}.vault.azure.net/"), new DefaultAzureCredential()), new KeyVaultSecretManager());
        remoteConfig = builder.Build();

        var secretName = (prefix + BaseConfigFile).Replace(".", "-");
        ReadConfigFromJson(remoteConfig[secretName]);
        foreach(var poolConfig in clusterConfig.Pools)
        {
            foreach(var p in poolConfig.Ports)
            {
                try
                {
                    if(!p.Value.Tls) continue;
                    var cert = remoteConfig[p.Value.TlsPfxFile];
                    if(cert == null) continue;
                    p.Value.TlsPfx = new X509Certificate2(Convert.FromBase64String(cert), (string) null, X509KeyStorageFlags.MachineKeySet);
                    Console.WriteLine("Successfully loaded TLS certificate from key vault...");
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"Failed to load TLS certificate from key vault, Error={ex.Message}");
                }
            }
        }
        ValidateConfig();

        return clusterConfig;
    }

    private static void HumanizeJsonParseException(JsonSerializationException ex)
    {
        var m = RegexJsonTypeConversionError.Match(ex.Message);

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

    private static class AppConfigConstants
    {
        public const string PersistencePostgresUser = "persistence.postgres.user";
        public static readonly string PersistencePostgresPassword = "persistence.postgres.password";
        public static readonly string CoinBasePassword = "pools.{0}.paymentProcessing.coinbasePassword";
        public static readonly string PrivateKey = "pools.{0}.paymentProcessing.PrivateKey";
        public static readonly string TlsPfxFile = "pools.{0}.{1}.tlsPfxFile";
        public static readonly string EtherscanApiKey = "pools.{0}.etherscan.apiKey";
        public static readonly string PersistenceCosmosAuthorizationKey = "persistence.cosmos.authorizationKey";
    }
}
