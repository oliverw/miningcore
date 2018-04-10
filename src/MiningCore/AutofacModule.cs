/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System.Linq;
using System.Reflection;
using Autofac;
using MiningCore.Api;
using MiningCore.Banning;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.Bitcoin.DaemonResponses;
using MiningCore.Blockchain.BitcoinGold;
using MiningCore.Blockchain.Dash;
using MiningCore.Blockchain.Dash.DaemonResponses;
using MiningCore.Blockchain.Ethereum;
using MiningCore.Blockchain.Flo;
using MiningCore.Blockchain.Monero;
using MiningCore.Blockchain.Straks;
using MiningCore.Blockchain.Straks.DaemonResponses;
using MiningCore.Blockchain.ZCash;
using MiningCore.Blockchain.ZCash.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.Mining;
using MiningCore.Notifications;
using MiningCore.Payments;
using MiningCore.Payments.PaymentSchemes;
using MiningCore.Time;
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

            builder.RegisterType<PayoutManager>()
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<StandardClock>()
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterType<IntegratedBanManager>()
                .Keyed<IBanManager>(BanManagerKind.Integrated)
                .SingleInstance();

            builder.RegisterType<ShareRecorder>()
                .SingleInstance();

            builder.RegisterType<ShareRelay>()
                .SingleInstance();

            builder.RegisterType<ApiServer>()
                .SingleInstance();

            builder.RegisterType<StatsRecorder>()
                .AsSelf();

            builder.RegisterType<NotificationService>()
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

            builder.RegisterType<PPLNSPaymentScheme>()
                .Keyed<IPayoutScheme>(PayoutScheme.PPLNS)
                .SingleInstance();

            builder.RegisterType<SoloPaymentScheme>()
                .Keyed<IPayoutScheme>(PayoutScheme.Solo)
                .SingleInstance();

            //////////////////////
            // Bitcoin and family

            builder.RegisterType<BitcoinJobManager<BitcoinJob<BlockTemplate>, BlockTemplate>>()
                .AsSelf();

            builder.RegisterType<BitcoinJobManager<DashJob, DashBlockTemplate>>()
                .AsSelf();

            builder.RegisterType<BitcoinJobManager<StraksJob, StraksBlockTemplate>>()
                .AsSelf();

            builder.RegisterType<BitcoinJobManager<ZCashJob, ZCashBlockTemplate>>()
                .AsSelf();

            builder.RegisterType<BitcoinJobManager<FloJob, BlockTemplate>>()
                .AsSelf();

            //////////////////////
            // Flo
            
            builder.RegisterType<FloJobManager>()
                .AsSelf();

            //////////////////////
            // Monero

            builder.RegisterType<MoneroJobManager>()
                .AsSelf();

            //////////////////////
            // Ethereum

            builder.RegisterType<EthereumJobManager>()
                .AsSelf();

            //////////////////////
            // ZCash

            builder.RegisterType<ZCashJobManager<ZCashJob>>()
                .AsSelf();

            //////////////////////
            // Bitcoin Gold

            builder.RegisterType<BitcoinGoldJobManager>()
                .AsSelf();

            base.Load(builder);
        }
    }
}
