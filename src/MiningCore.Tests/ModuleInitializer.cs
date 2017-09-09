using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Autofac;
using AutoMapper;

namespace MiningCore.Tests
{
    public static class ModuleInitializer
    {
        private static readonly object initLock = new object();

        private static bool isInitialized = false;
        private static IContainer container;

        public static IContainer Container => container;

        /// <summary>
        /// Initializes the module.
        /// </summary>
        public static void Initialize()
        {
            lock (initLock)
            {
                if (isInitialized)
                    return;

                Program.PreloadNativeLibs();

                var builder = new ContainerBuilder();

                builder.RegisterAssemblyModules(typeof(AutofacModule).GetTypeInfo().Assembly);

                // AutoMapper
                var amConf = new MapperConfiguration(cfg => { cfg.AddProfile(new AutoMapperProfile()); });

                builder.Register((ctx, parms) => amConf.CreateMapper());

                // Autofac Container
                container = builder.Build();

                isInitialized = true;
            }
        }
    }
}
