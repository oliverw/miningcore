using System.Linq;
using System.Reflection;
using Autofac;
using Module = Autofac.Module;

namespace MiningCore.Transport.LibUv
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

            builder.RegisterAssemblyTypes(thisAssembly)
                .Where(t => t.GetInterfaces()
                    .Any(i =>
                        i.IsAssignableFrom(typeof(IEndpointDispatcher))))
                .AsImplementedInterfaces();

            base.Load(builder);
        }
    }
}
