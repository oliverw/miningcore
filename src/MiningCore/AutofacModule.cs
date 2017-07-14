using System;
using System.Reflection;
using Autofac;
using MiningCore.JsonRpc;
using MiningCore.Protocols.JsonRpc;
using Module = Autofac.Module;

namespace MiningCore
{
    public class AutofacModule : Module
    {
        /// <summary>
        /// Override to add registrations to the container.
        /// </summary>
        /// <remarks>
        /// Note that the ContainerBuilder parameter is unique to this module.
        /// </remarks>
        /// <param name="builder">The builder through which components can be registered.</param>
        protected override void Load(ContainerBuilder builder)
        {
            var thisAssembly = typeof(AutofacModule).GetTypeInfo().Assembly;

            builder.RegisterType<Pool>()
                .AsSelf();

            base.Load(builder);
        }
    }
}
