using Microsoft.Extensions.Caching.Memory;
using Miningcore.Contracts;
using Miningcore.Nicehash.API;
using Miningcore.Rest;
using NLog;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Nicehash;

public class NicehashService
{
    public NicehashService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache)
    {
        this.cache = cache;
        client = new SimpleRestClient(httpClientFactory, NicehashConstants.ApiBaseUrl);
    }

    private readonly SimpleRestClient client;
    private readonly IMemoryCache cache;

    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

    public Task<double?> GetStaticDiff(string coin, string algo, CancellationToken ct)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(coin));
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(algo));

        return Guard(async () =>
        {
            var algos = await cache.GetOrCreateAsync("nicehash_algos", async (entry) =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);

                // query nicehash API
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(3000);

                var response = await client.Get<NicehashMiningAlgorithmsResponse>("/mining/algorithms", cts.Token);

                // transform
                return response.Algorithms.ToDictionary(x => x.Algorithm, x=> x, StringComparer.InvariantCultureIgnoreCase);
            });

            var niceHashAlgo = GetNicehashAlgo(coin, algo);

            if(!algos.TryGetValue(niceHashAlgo, out var item))
                return (double?) null;

            return item.MinimalPoolDifficulty;
        }, ex=> logger.Error(()=> $"Error updating Nicehash diffs: {ex.Message}"));
    }

    private string GetNicehashAlgo(string coin, string algo)
    {
        if(coin == "Monero" && algo == "RandomX")
            return "randomxmonero";

        return algo;
    }
}
