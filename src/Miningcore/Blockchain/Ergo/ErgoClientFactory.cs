using System;
using System.Linq;
using System.Net.Http;
using Miningcore.Blockchain.Ergo.Configuration;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Util;
using NLog;

namespace Miningcore.Blockchain.Ergo
{
    public static class ErgoClientFactory
    {
        public static ErgoClient CreateClient(PoolConfig poolConfig, ClusterConfig clusterConfig, IHttpClientFactory httpClientFactory, ILogger logger)
        {
            var epConfig = poolConfig.Daemons.First();
            var extra = epConfig.Extra.SafeExtensionDataAs<ErgoDaemonEndpointConfigExtra>();

            if(logger != null && clusterConfig.PaymentProcessing?.Enabled == true &&
                poolConfig.PaymentProcessing?.Enabled == true && string.IsNullOrEmpty(extra?.ApiKey))
                logger.ThrowLogPoolStartupException("Ergo daemon apiKey not provided");

            var baseUrl = new UriBuilder(epConfig.Ssl || epConfig.Http2 ? Uri.UriSchemeHttps : Uri.UriSchemeHttp,
                epConfig.Host, epConfig.Port, epConfig.HttpPath);

            var result = new ErgoClient(baseUrl.ToString(), httpClientFactory.CreateClient())
            {
                ApiKey = extra.ApiKey
            };

#if DEBUG
            result.ReadResponseAsString = true;
#endif
            return result;
        }
    }
}
