using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Features.Metadata;
using AutoMapper;
using FluentValidation;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Miningcore.Api;
using Miningcore.Api.Controllers;
using Miningcore.Api.Responses;
using Miningcore.Configuration;
using Miningcore.Crypto.Hashing.Equihash;
using Miningcore.DataStore.FileLogger;
using Miningcore.DataStore.Postgres;
using Miningcore.Mining;
using Miningcore.Notifications;
using Miningcore.Payments;
using Miningcore.PoolCore;
using Miningcore.Util;
using NBitcoin.Zcash;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using NLog.Conditions;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;
using Microsoft.Extensions.Logging;
using LogLevel = NLog.LogLevel;
using ILogger = NLog.ILogger;
using NLog.Extensions.Logging;
using Prometheus;
using WebSocketManager;
using Miningcore.Api.Middlewares;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using AspNetCoreRateLimit;

namespace Miningcore.PoolCore
{
    public class Pool
    {
        private static readonly CancellationTokenSource cts = new CancellationTokenSource();
        private static readonly ILogger logger = LogManager.GetLogger("PoolCore");
        private static IContainer container;
        private static ShareRecorder shareRecorder;
        private static ShareRelay shareRelay;
        private static ShareReceiver shareReceiver;
        private static PayoutManager payoutManager;
        private static StatsRecorder statsRecorder;
        public static ClusterConfig clusterConfig;
        private static IWebHost webHost;
        private static NotificationService notificationService;
        private static MetricsPublisher metricsPublisher;
        private static BtStreamReceiver btStreamReceiver;
        private static readonly ConcurrentDictionary<string, IMiningPool> pools = new ConcurrentDictionary<string, IMiningPool>();
        private static AdminGcStats gcStats = new AdminGcStats();
        private static readonly IPAddress IPv4LoopBackOnIPv6 = IPAddress.Parse("::ffff:127.0.0.1");

        public static void Start(string configFile)
        {
            try
            {
                // log unhandled program exception errors
                AppDomain currentDomain = AppDomain.CurrentDomain;
                currentDomain.UnhandledException += new UnhandledExceptionEventHandler(MC_UnhandledException);
                currentDomain.ProcessExit += OnProcessExit;
                Console.CancelKeyPress += new ConsoleCancelEventHandler(OnCancelKeyPress);

                // Check valid OS and user
                ValidateRuntimeEnvironment();

                // Miningcore Pool Logo
                PoolLogo.Logo();

                // Read config.json file
                clusterConfig = PoolConfig.GetConfigContent(configFile);

                // Initialize Logging
                //ConfigureLogging();
                FileLogger.ConfigureLogging();

                // LogRuntimeInfo();
                //-----------------------------------------------------------------------------
                logger.Info(() => $"{RuntimeInformation.FrameworkDescription.Trim()} on {RuntimeInformation.OSDescription.Trim()} [{RuntimeInformation.ProcessArchitecture}]");

                // Bootstrap();
                //-----------------------------------------------------------------------------
                ZcashNetworks.Instance.EnsureRegistered();

                // Service collection
                var builder = new ContainerBuilder();
                builder.RegisterAssemblyModules(typeof(AutofacModule).GetTypeInfo().Assembly);
                builder.RegisterInstance(clusterConfig);
                builder.RegisterInstance(pools);
                builder.RegisterInstance(gcStats);

                // AutoMapper
                var amConf = new MapperConfiguration(cfg => { cfg.AddProfile(new AutoMapperProfile()); });
                builder.Register((ctx, parms) => amConf.CreateMapper());

                PostgresInterface.ConnectDatabase(builder);
                container = builder.Build();

                // Configure Equihash
                if(clusterConfig.EquihashMaxThreads.HasValue)
                    EquihashSolver.MaxThreads = clusterConfig.EquihashMaxThreads.Value;

                MonitorGarbageCollection();

                // Start Miningcore Pool
                if(!cts.IsCancellationRequested)
                    StartMiningcorePool().Wait(cts.Token);
    
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
                if(!(ex.InnerExceptions.First() is PoolStartupAbortException))
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

            finally
            {
                Shutdown();
                
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

        private static void MonitorGarbageCollection()
        {
            var thread = new Thread(() =>
            {
                var sw = new Stopwatch();

                while(true)
                {
                    var s = GC.WaitForFullGCApproach();
                    if(s == GCNotificationStatus.Succeeded)
                    {
                        logger.Info(() => "Garbage Collection bin Full soon");
                        sw.Start();
                    }

                    s = GC.WaitForFullGCComplete();

                    if(s == GCNotificationStatus.Succeeded)
                    {
                        logger.Info(() => "Garbage Collection bin Full!!");

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

      
        private static Dictionary<string, CoinTemplate> LoadCoinTemplates()
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
            var ipList = whitelist?.Select(x => IPAddress.Parse(x)).ToList();
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
                    new RateLimitRule
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

        private static void StartApi()
        {
            var address = clusterConfig.Api?.ListenAddress != null
                ? (clusterConfig.Api.ListenAddress != "*" ? IPAddress.Parse(clusterConfig.Api.ListenAddress) : IPAddress.Any)
                : IPAddress.Parse("127.0.0.1");

            var port = clusterConfig.Api?.Port ?? 4000;
            var enableApiRateLimiting = !(clusterConfig.Api?.RateLimiting?.Disabled == true);

            webHost = WebHost.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    // NLog
                    logging.ClearProviders();
                    logging.AddNLog();

                    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                })
                .ConfigureServices(services =>
                {
                    // Memory Cache
                    services.AddMemoryCache();

                    // rate limiting
                    if(enableApiRateLimiting)
                    {
                        
                        services.Configure<IpRateLimitOptions>(ConfigureIpRateLimitOptions);
                        services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
                        services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
                        services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
                    }

                    // Controllers
                    services.AddSingleton<PoolApiController, PoolApiController>();
                    services.AddSingleton<AdminApiController, AdminApiController>();

                    // MVC
                    services.AddSingleton((IComponentContext) container);
                    services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

                    services.AddControllers()
                        .SetCompatibilityVersion(CompatibilityVersion.Version_3_0)
                        .AddControllersAsServices()
                        .AddNewtonsoftJson(options =>
                        {
                            options.SerializerSettings.Formatting = Formatting.Indented;
                        });

                    // .ContractResolver = new DefaultContractResolver());

                    // Gzip Compression
                    services.AddResponseCompression();

                    // Cors
                    // ToDo: Test if Admin portal is working without .credentials()
                    // .AllowAnyOrigin(_ => true)
                    // .AllowCredentials()
                    services.AddCors(options =>
                    {
                        options.AddPolicy("CorsPolicy",
                            builder => builder.AllowAnyOrigin()
                                              .AllowAnyMethod()
                                              .AllowAnyHeader()
                                          );
                    }
                    );

                    // WebSockets
                    services.AddWebSocketManager();
                })
                .Configure(app =>
                {
                    if(enableApiRateLimiting)
                        app.UseIpRateLimiting();

                    app.UseMiddleware<ApiExceptionHandlingMiddleware>();

                    UseIpWhiteList(app, true, new[] { "/api/admin" }, clusterConfig.Api?.AdminIpWhitelist);
                    UseIpWhiteList(app, true, new[] { "/metrics" }, clusterConfig.Api?.MetricsIpWhitelist);

                    app.UseResponseCompression();
                    app.UseCors("CorsPolicy");
                    app.UseWebSockets();
                    app.MapWebSocketManager("/notifications", app.ApplicationServices.GetService<WebSocketNotificationsRelay>());
                    app.UseMetricServer();
                    //app.UseMvc();
                    app.UseRouting();
                    app.UseEndpoints(endpoints => {
                        endpoints.MapDefaultControllerRoute();
                        endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
                    });
                })
                .UseKestrel(options =>
                {
                    options.Listen(address, clusterConfig.Api.Port, listenOptions =>
                    {
                        if(clusterConfig.Api.SSLConfig?.Enabled == true)
                            listenOptions.UseHttps(clusterConfig.Api.SSLConfig.SSLPath, clusterConfig.Api.SSLConfig.SSLPassword);
                    });
                })
                .Build();

            webHost.Start();

            logger.Info(() => $"API Online @ {address}:{port}{(!enableApiRateLimiting ? " [rate-limiting disabled]" : string.Empty)}");
            logger.Info(() => $"Prometheus Metrics Online @ {address}:{port}/metrics");
            logger.Info(() => $"WebSocket notifications streaming @ {address}:{port}/notifications");
        }

        private static async Task StartMiningcorePool()
        {
            var coinTemplates = LoadCoinTemplates();
            logger.Info($"{coinTemplates.Keys.Count} coins loaded from {string.Join(", ", clusterConfig.CoinTemplates)}");

            // Populate pool configs with corresponding template
            foreach(var poolConfig in clusterConfig.Pools.Where(x => x.Enabled))
            {
                // Foreach coin definition
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
                await Task.Run(() => StartApi() );

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
                //apiServer?.AttachPool(pool);

                await pool.StartAsync(cts.Token);
            }));

            // keep running
            await Observable.Never<Unit>().ToTask(cts.Token);
        }

        public static Task RecoverSharesAsync(string recoveryFilename)
        {
            shareRecorder = container.Resolve<ShareRecorder>();
            return shareRecorder.RecoverSharesAsync(clusterConfig, recoveryFilename);
        }

        // log unhandled program exception errors
        private static void MC_UnhandledException(object sender, UnhandledExceptionEventArgs args )
        {
            if(logger != null)
            {
                logger.Error(args.ExceptionObject);
                LogManager.Flush(TimeSpan.Zero);
            }
            Exception e = (Exception) args.ExceptionObject;
            Console.WriteLine("----------------------------------------------------------------------------------------");
            Console.WriteLine("MyHandler caught : " + e.Message);
            Console.WriteLine("Runtime terminating: {0}", args.IsTerminating);
        }

        protected static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs args )
        {
            logger?.Info(() => $"Miningcore is stopping because exit key [{args.SpecialKey}] recieved. Exiting.");
            Console.WriteLine($"Miningcore is stopping because exit key  [{args.SpecialKey}] recieved. Exiting.");

            try
            {
                cts?.Cancel();
            }
            catch
            {
            }

            args.Cancel = true;
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            logger?.Info(() => "Miningcore received process stop request.");
            Console.WriteLine("Miningcore received process stop request.");

            try
            {
                cts?.Cancel();
            }
            catch
            {
            }
        }

        private static void Shutdown()
        {
            Console.WriteLine("Miningcore is shuting down... bye!");
            logger?.Info(() => "Miningcore is shuting down... bye!");
            
            foreach(var pool in pools.Values)
            {
                Console.WriteLine($"Stopping pool {pool}");
                pool.Stop();
            }
                
            shareRelay?.Stop();
            shareReceiver?.Stop();
            shareRecorder?.Stop();
            statsRecorder?.Stop();
            Process.GetCurrentProcess().Kill();
        }
    }
}
