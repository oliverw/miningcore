using System.Net;
using System.Net.Http;
using System.Reflection;
using Autofac;
using MiningCore.Blockchain;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Configuration;
using MiningCore.MiningPool;
using MiningCore.Stratum;
using MiningCore.Stratum.Authorization;
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

            builder.RegisterType<BitcoinDaemon>()
                .Named<IBlockchainDemon>(BlockchainFamily.Bitcoin.ToString())
                .AsImplementedInterfaces();
            
            builder.RegisterType<AddressBasedStratumAuthorizer>()
                .Named<IStratumAuthorizer>(StratumAuthorizerKind.AddressBased.ToString())
                .AsImplementedInterfaces()
                .SingleInstance();

            builder.RegisterInstance(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });


            base.Load(builder);
        }
    }
}
