/*
MiningCore 2.0
Copyright 2021 MinerNL (Miningcore.com)
*/

using Autofac;
using Miningcore.Configuration;
using Miningcore.Persistence.Dummy;
using Miningcore.Persistence.Postgres;
using Miningcore.Persistence.Postgres.Repositories;
using Miningcore.PoolCore;
using Miningcore.Util;
using ILogger = NLog.ILogger;
using NLog.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NLog;

namespace Miningcore.DataStore.Postgres
{
    internal class PostgresInterface
    {

        private static readonly ILogger logger = LogManager.GetLogger("Postgres");

        internal static void ConnectDatabase(ContainerBuilder builder)
        {
            ConfigurePersistence(builder);
        }



        private static void ConfigurePersistence(ContainerBuilder builder)
        {
            if(Pool.clusterConfig.Persistence == null && Pool.clusterConfig.PaymentProcessing?.Enabled == true && Pool.clusterConfig.ShareRelay == null)
                logger.ThrowLogPoolStartupException("Persistence is not configured!");

            if(Pool.clusterConfig.Persistence?.Postgres != null)
            {
                ConfigurePostgres(Pool.clusterConfig.Persistence.Postgres, builder);
            }
            else
            {
                ConfigureDummyPersistence(builder);
            }

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

            logger.Info(() => $"Connecting to Postgres Server {pgConfig.Host}:{pgConfig.Port} Database={pgConfig.Database} User={pgConfig.User}");

            // build connection string
            var connectionString = $"Server={pgConfig.Host};Port={pgConfig.Port};Database={pgConfig.Database};User Id={pgConfig.User};Password={pgConfig.Password};CommandTimeout=900;";
            
            // concatenate SSL config to connectionString
            if(pgConfig.Ssl == true)
                connectionString += "SSL Mode=Require;Trust Server Certificate=True;Server Compatibility Mode=Redshift;";

            // register connection factory
            builder.RegisterInstance(new PgConnectionFactory(connectionString))
                .AsImplementedInterfaces();

            // register repositories
            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.Namespace.StartsWith(typeof(ShareRepository).Namespace))
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
                .Where(t => t.Namespace.StartsWith(typeof(ShareRepository).Namespace))
                .AsImplementedInterfaces()
                .SingleInstance();
        }
    }
}
