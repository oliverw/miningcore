using System.Linq;
using System.Reflection;
using Autofac;
using Miningcore.Api;
using Miningcore.Banning;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Cryptonote;
using Miningcore.Blockchain.Equihash;
using Miningcore.Blockchain.Ethereum;
using Miningcore.Configuration;
using Miningcore.Crypto;
using Miningcore.Crypto.Hashing.Equihash;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Notifications;
using Miningcore.Payments;
using Miningcore.Payments.PaymentSchemes;
using Miningcore.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Module = Autofac.Module;
using Microsoft.AspNetCore.Mvc;
using Miningcore.Nicehash;

namespace Miningcore
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

            builder.RegisterType<MessageBus>()
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterType<StandardClock>()
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterType<IntegratedBanManager>()
                .Keyed<IBanManager>(BanManagerKind.Integrated)
                .SingleInstance();

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.GetCustomAttributes<CoinFamilyAttribute>().Any() && t.GetInterfaces()
                    .Any(i =>
                        i.IsAssignableFrom(typeof(IMiningPool)) ||
                        i.IsAssignableFrom(typeof(IPayoutHandler)) ||
                        i.IsAssignableFrom(typeof(IPayoutScheme))))
                .WithMetadataFrom<CoinFamilyAttribute>()
                .AsImplementedInterfaces();

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.GetInterfaces().Any(i => i.IsAssignableFrom(typeof(IHashAlgorithm))))
                .PropertiesAutowired()
                .AsSelf();

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.IsAssignableTo<EquihashSolver>())
                .PropertiesAutowired()
                .AsSelf();

            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.IsAssignableTo<ControllerBase>())
                .PropertiesAutowired()
                .AsSelf();

            builder.RegisterType<WebSocketNotificationsRelay>()
                .PropertiesAutowired()
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<NicehashService>()
                .SingleInstance();

            //////////////////////
            // Background services

            builder.RegisterType<PayoutManager>()
                .SingleInstance();

            builder.RegisterType<ShareRecorder>()
                .SingleInstance();

            builder.RegisterType<ShareReceiver>()
                .SingleInstance();

            builder.RegisterType<BtStreamReceiver>()
                .SingleInstance();

            builder.RegisterType<ShareRelay>()
                .SingleInstance();

            builder.RegisterType<StatsRecorder>()
                .SingleInstance();

            builder.RegisterType<NotificationService>()
                .SingleInstance();

            builder.RegisterType<MetricsPublisher>()
                .SingleInstance();

            //////////////////////
            // Payment Schemes

            builder.RegisterType<PPLNSPaymentScheme>()
                .Keyed<IPayoutScheme>(PayoutScheme.PPLNS)
                .SingleInstance();

            builder.RegisterType<SOLOPaymentScheme>()
                .Keyed<IPayoutScheme>(PayoutScheme.SOLO)
                .SingleInstance();

            builder.RegisterType<PROPPaymentScheme>()
                .Keyed<IPayoutScheme>(PayoutScheme.PROP)
                .SingleInstance();

            //////////////////////
            // Bitcoin and family

            builder.RegisterType<BitcoinJobManager>()
                .AsSelf();

            //////////////////////
            // Cryptonote

            builder.RegisterType<CryptonoteJobManager>()
                .AsSelf();

            //////////////////////
            // Ethereum

            builder.RegisterType<EthereumJobManager>()
                .AsSelf();


            //////////////////////
            // ZCash

            builder.RegisterType<EquihashJobManager>()
                .AsSelf();

            base.Load(builder);
        }
    }
}
