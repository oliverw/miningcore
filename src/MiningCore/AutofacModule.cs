using System.Net;
using System.Net.Http;
using System.Reflection;
using Autofac;
using MiningCore.Net;
using MiningCore.Stratum;
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

            builder.Register(c =>
            {
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
                };

                return new HttpClient(handler);
            })
            .AsSelf();

            builder.RegisterType<Pool>()
                .AsSelf();

            builder.RegisterType<StratumServer>()
                .AsSelf();

            builder.RegisterType<StratumClient>()
                .AsSelf();

            base.Load(builder);
        }
    }
}
