using System.Text;
using Miningcore.Blockchain.Ethereum;
using Miningcore.Contracts;
using Miningcore.Native;
using NLog;

namespace Miningcore.Crypto.Hashing.Ethash.Etchash;

[Identifier("etchash")]
public class EtchashLight : IEthashLight
{
    public void Setup(int totalCache, ulong hardForkBlock, string dagDir = null)
    {
        this.numCaches = totalCache;
        this.hardForkBlock = hardForkBlock;
        this.dagDir = dagDir;
    }

    private int numCaches; // Maximum number of caches to keep before eviction (only init, don't modify)
    private readonly object cacheLock = new();
    private readonly Dictionary<ulong, Cache> caches = new();
    private Cache future;
    private string dagDir;
    private ulong hardForkBlock;
    public string AlgoName { get; } = "Etchash";

    public void Dispose()
    {
        foreach(var value in caches.Values)
            value.Dispose();
    }

    public async Task<IEthashCache> GetCacheAsync(ILogger logger, ulong block, CancellationToken ct)
    {
        var epochLength = block >= this.hardForkBlock ? EthereumClassicConstants.EpochLength : EthereumConstants.EpochLength;
        logger.Debug(() => $"Epoch length used: {epochLength}");
        var epoch = block / epochLength;
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
                    result = new Cache(epoch, dagDir);
                }

                caches[epoch] = result;
            }

            // If we used up the future cache, or need a refresh, regenerate
            else if(future == null || future.Epoch <= epoch)
            {
                logger.Info(() => $"Pre-generating cache for epoch {epoch + 1}");
                future = new Cache(epoch + 1, dagDir);

#pragma warning disable 4014
                future.GenerateAsync(logger, epochLength, this.hardForkBlock, ct);
#pragma warning restore 4014
            }

            result.LastUsed = DateTime.Now;
        }

        // get/generate current one
        await result.GenerateAsync(logger, epochLength, this.hardForkBlock, ct);

        return result;
    }
}
