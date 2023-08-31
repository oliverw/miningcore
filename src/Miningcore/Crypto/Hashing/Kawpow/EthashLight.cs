using Miningcore.Blockchain.Ravencoin;
using NLog;

namespace Miningcore.Crypto.Hashing.Kawpow;

public class EthashLight : IDisposable
{
    public void Setup(int numCaches)
    {
        this.numCaches = numCaches;
    }

    private int numCaches; // Maximum number of caches to keep before eviction (only init, don't modify)
    private readonly object cacheLock = new();
    private readonly Dictionary<int, Cache> caches = new();
    private Cache future;
    public string AlgoName { get; } = "KawPow";

    public void Dispose()
    {
        foreach(var value in caches.Values)
            value.Dispose();
    }

    public async Task<Cache> GetCacheAsync(ILogger logger, int block)
    {
        var epoch = block / RavencoinConstants.EpochLength;
        Cache result;

        lock(cacheLock)
        {
            if(numCaches == 0)
                numCaches = 3;

            if(!caches.TryGetValue(epoch, out result))
            {
                // No cached cache, evict the oldest if the cache limit was reached
                while(caches.Count >= numCaches)
                {
                    var toEvict = caches.Values.OrderBy(x => x.LastUsed).First();
                    var key = caches.First(pair => pair.Value == toEvict).Key;
                    var epochToEvict = toEvict.Epoch;

                    logger.Info(() => $"Evicting cache for epoch {epochToEvict} in favour of epoch {epoch}");
                    toEvict.Dispose();
                    caches.Remove(key);
                }

                // If we have the new cache pre-generated, use that, otherwise create a new one
                if(future != null && future.Epoch == epoch)
                {
                    logger.Debug(() => $"Using pre-generated cache for epoch {epoch}");

                    result = future;
                    future = null;
                }

                else
                {
                    logger.Info(() => $"No pre-generated cache available, creating new for epoch {epoch}");
                    result = new Cache(epoch);
                }

                caches[epoch] = result;
            }

            // If we used up the future cache, or need a refresh, regenerate
            else if(future == null || future.Epoch <= epoch)
            {
                logger.Info(() => $"Pre-generating cache for epoch {epoch + 1}");
                future = new Cache(epoch + 1);

#pragma warning disable 4014
                future.GenerateAsync(logger);
#pragma warning restore 4014
            }

            result.LastUsed = DateTime.Now;
        }

        // get/generate current one
        await result.GenerateAsync(logger);

        return result;
    }
}
