using System.Linq;
using System.Reflection;
using Autofac;
using MiningCore.Api;
using MiningCore.Banning;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.Monero;
using MiningCore.Configuration;
using MiningCore.DaemonInterface;
using MiningCore.JsonRpc;
using MiningCore.Mining;
using MiningCore.Payments;
using MiningCore.Payments.PayoutSchemes;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
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

            builder.RegisterInstance(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });

            builder.RegisterType<JsonRpcConnection>()
                .AsSelf();

            builder.RegisterType<DaemonClient>()
                .AsSelf();

            builder.RegisterType<PayoutProcessor>()
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<BitcoinExtraNonceProvider>()
                .AsSelf();

            builder.RegisterType<IntegratedBanManager>()
                .Keyed<IBanManager>(BanManagerKind.Integrated)
                .SingleInstance();

            builder.RegisterType<ShareRecorder>()
                .SingleInstance();

            builder.RegisterType<ApiServer>()
                .SingleInstance();

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.GetCustomAttributes<CoinMetadataAttribute>().Any() && t.GetInterfaces()
                                .Any(i =>
                                    i.IsAssignableFrom(typeof(IMiningPool)) ||
                                    i.IsAssignableFrom(typeof(IPayoutHandler)) ||
                                    i.IsAssignableFrom(typeof(IPayoutScheme))))
                .WithMetadataFrom<CoinMetadataAttribute>()
                .AsImplementedInterfaces();

            //////////////////////
            // Payment Schemes

            builder.RegisterType<PayPerLastNShares>()
                .Keyed<IPayoutScheme>(PayoutScheme.PPLNS)
                .SingleInstance();

            //////////////////////
            // Bitcoin and family

            builder.RegisterType<BitcoinJobManager>()
                .AsSelf();

            //////////////////////
            // Monero

            builder.RegisterType<MoneroJobManager>()
                .AsSelf();

            base.Load(builder);
        }
    }
}
