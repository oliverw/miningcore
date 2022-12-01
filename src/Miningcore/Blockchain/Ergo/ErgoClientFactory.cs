using System.Net;
using System.Net.Http.Headers;
using Miningcore.Blockchain.Ergo.Configuration;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Mining;
using NLog;

namespace Miningcore.Blockchain.Ergo;

public static class ErgoClientFactory
{
    public static ErgoClient CreateClient(PoolConfig poolConfig, ClusterConfig clusterConfig, ILogger logger)
    {
        var epConfig = poolConfig.Daemons.First();
        var extra = epConfig.Extra.SafeExtensionDataAs<ErgoDaemonEndpointConfigExtra>();

        if(logger != null && clusterConfig.PaymentProcessing?.Enabled == true &&
           poolConfig.PaymentProcessing?.Enabled == true && string.IsNullOrEmpty(extra?.ApiKey))
            throw new PoolStartupException("Ergo daemon apiKey not provided", poolConfig.Id);

        var baseUrl = new UriBuilder(epConfig.Ssl || epConfig.Http2 ? Uri.UriSchemeHttps : Uri.UriSchemeHttp,
            epConfig.Host, epConfig.Port, epConfig.HttpPath);

        var result = new ErgoClient(baseUrl.ToString(), new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,

            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
        }));

        if(!string.IsNullOrEmpty(extra?.ApiKey))
            result.RequestHeaders["api_key"] = extra.ApiKey;

        if(!string.IsNullOrEmpty(epConfig.User))
        {
            var auth = $"{epConfig.User}:{epConfig.Password}";
            result.RequestHeaders["Authorization"] = new AuthenticationHeaderValue("Basic", auth.ToByteArrayBase64()).ToString();
        }
#if DEBUG
        result.ReadResponseAsString = true;
#endif
        return result;
    }
}
