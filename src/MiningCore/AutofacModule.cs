using System.Net;
using System.Net.Http;
using System.Reflection;
using Autofac;
using MiningCore.Authorization;
using MiningCore.Blockchain;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Configuration;
using MiningCore.JsonRpc;
using MiningCore.MiningPool;
using MiningCore.Stratum;
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

            builder.RegisterType<JsonRpcConnection>()
                .AsSelf();

            builder.RegisterType<AddressBasedAuthorizer>()
                .Named<IStratumAuthorizer>(StratumAuthorizerKind.AddressBased.ToString())
                .SingleInstance();

            builder.RegisterType<ExtraNonceProvider>()
                .AsSelf();

            builder.RegisterInstance(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });

            //////////////////////
            // Bitcoin and family

            builder.RegisterType<BitcoinJobManager>()
                .Named<IMiningJobManager>("bitcoin")
                .Named<IMiningJobManager>("litecoin");

            builder.RegisterType<BitcoinDaemon>()
                .Named<IBlockchainDemon>("bitcoin")
                .Named<IBlockchainDemon>("litecoin");

            base.Load(builder);
        }
    }
}
