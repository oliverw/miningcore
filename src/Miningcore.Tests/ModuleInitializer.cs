using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Ethereum;
using Miningcore.Configuration;
using Miningcore.Native;
using Miningcore.Tests.Persistence.Postgres;
using Miningcore.Tests.Persistence.Postgres.Repositories;
using Moq;
using Newtonsoft.Json;

namespace Miningcore.Tests;

public static class ModuleInitializer
{
    private static readonly object initLock = new object();

    private static bool isInitialized = false;
    private static IContainer container;
    private static Dictionary<string, CoinTemplate> coinTemplates;

    public static IContainer Container => container;
    public static Dictionary<string, CoinTemplate> CoinTemplates => coinTemplates;

    /// <summary>
    /// Initializes the module.
    /// </summary>
    public static void Initialize()
    {
        lock(initLock)
        {
            if(isInitialized)
                return;

            var builder = new ContainerBuilder();

            builder.RegisterAssemblyModules(typeof(AutofacModule).GetTypeInfo().Assembly);

            // AutoMapper
            var amConf = new MapperConfiguration(cfg => { cfg.AddProfile(new AutoMapperProfile()); });

            builder.Register((ctx, parms) => amConf.CreateMapper());

            ClusterConfig clusterConfig = null;
            try 
            {
                clusterConfig = JsonConvert.DeserializeObject<ClusterConfig>(File.ReadAllText("config_test.json"));
            }
            catch (FileNotFoundException ex)
            {
                clusterConfig = JsonConvert.DeserializeObject<ClusterConfig>(File.ReadAllText("../../../config_test.json"));
            }

            // Register ClusterConfig and IExtraNonceProvider
            if (null != clusterConfig)
            {
                builder.RegisterInstance(clusterConfig);
                var poolId = clusterConfig.Pools[0].Id;
                var clusterInstanceId = clusterConfig.InstanceId;
                builder.RegisterInstance(new EthereumExtraNonceProvider(poolId, clusterInstanceId)).AsImplementedInterfaces();
            }
            
            // Register IConnectionFactory
            var dbConnection = new Mock<IDbConnection>().Object;
            builder.RegisterInstance(new MockConnectionFactory(dbConnection)).AsImplementedInterfaces();

            // Register repositories
            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.Namespace != null && t.Namespace.StartsWith(typeof(ShareRepository).Namespace))
                .AsImplementedInterfaces()
                .SingleInstance();

            // Autofac Container
            container = builder.Build();

            isInitialized = true;

            // Load coin templates
            var basePath = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            var defaultDefinitions = Path.Combine(basePath, "coins.json");

            var coinDefs = new[]
            {
                defaultDefinitions
            };

            coinTemplates = CoinTemplateLoader.Load(container, coinDefs);

            foreach(var poolConfig in clusterConfig.Pools.Where(x => x.Enabled))
            {
                coinTemplates.TryGetValue(poolConfig.Coin, out var template);
                poolConfig.Template = template;
            }

            Cryptonight.InitContexts(1);
        }
    }
}
