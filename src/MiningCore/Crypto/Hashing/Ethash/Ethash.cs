using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using MiningCore.Blockchain.Ethereum;
using NLog;

namespace MiningCore.Crypto.Hashing.Ethash
{
    public class Ethash : IDisposable
    {
        public Ethash(int numCaches)
        {
            this.numCaches = numCaches;
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private Dag currentDag;
        private readonly object dagLock = new object();
        private int numCaches;  // Maximum number of caches to keep before eviction (only init, don't modify)
        private readonly object cacheLock = new object();
        private readonly Dictionary<ulong, Cache> caches = new Dictionary<ulong, Cache>();
        private Cache future;

        public void Dispose()
        {
            currentDag?.Dispose();
            currentDag = null;

            foreach (var value in caches.Values)
                value.Dispose();
        }

        public async Task<Dag> GetDagAsync(ulong block)
        {
            Dag result;
            var epoch = block / EthereumConstants.EpochLength;

            lock (dagLock)
            {
                if (currentDag != null && currentDag.Epoch == epoch)
                    result = currentDag;
                else
                {
                    result = new Dag(epoch);
                    currentDag = result;
                }
            }

            await result.GenerateAsync();
            return result;
        }

        public async Task<bool> VerifyAsync(Block block)
        {
            if (block.Height > EthereumConstants.EpochLength * 2048)
            {
                logger.Debug(() => $"Block height {block.Height} exceeds limit of {EthereumConstants.EpochLength * 2048}");
                return false;
            }

            if (block.Difficulty.CompareTo(BigInteger.Zero) == 0)
            {
                logger.Debug(() => $"Invalid block diff");
                return false;
            }

            // look up cache
            var cache = await GetCacheAsync(block.Height);

            // Recompute the hash using the cache.
            byte[] mixDigest;
            byte[] resultBytes;
            if (!cache.Compute(block.HashNoNonce, block.Nonce, out mixDigest, out resultBytes))
                return false;

            // avoid mixdigest malleability as it's not included in a block's "hashNononce"
            if (!block.MixDigest.SequenceEqual(mixDigest))
                return false;

            // The actual check.
            var target = BigInteger.Divide(EthereumConstants.BigMaxValue, block.Difficulty);
            var result = new BigInteger(resultBytes).CompareTo(target) <= 0;
            return result;
        }

        private async Task<Cache> GetCacheAsync(ulong block)
        {
            var epoch = block / EthereumConstants.EpochLength;
            Cache result;

            lock (cacheLock)
            {
                if (numCaches == 0)
                    numCaches = 3;

                if (!caches.TryGetValue(epoch, out result))
                {
                    // No cached DAG, evict the oldest if the cache limit was reached
                    while (caches.Count >= numCaches)
                    {
                        var toEvict = caches.Values.OrderBy(x => x.LastUsed).First();
                        var key = caches.First(pair => pair.Value == toEvict).Key;
                        var epochToEvict = toEvict.Epoch;

                        logger.Debug(() => $"Evicting DAG for epoch {epochToEvict} in favour of epoch {epoch}");
                        toEvict.Dispose();
                        caches.Remove(key);
                    }

                    // If we have the new DAG pre-generated, use that, otherwise create a new one
                    if (future != null && future.Epoch == epoch)
                    {
                        logger.Debug(() => $"Using pre-generated DAG for epoch {epoch}");

                        result = future;
                        future = null;
                    }

                    else
                    {
                        logger.Debug(() => $"No pre-generated DAG available, creating new for epoch {epoch}");
                        result = new Cache(epoch);
                    }

                    caches[epoch] = result;

                    // If we just used up the future cache, or need a refresh, regenerate
                    if (future == null || future.Epoch <= epoch)
                    {
                        logger.Debug(() => $"Pre-generating DAG for epoch {epoch + 1}");
                        future = new Cache(epoch + 1);

                        #pragma warning disable 4014
                        future.GenerateAsync();
                        #pragma warning restore 4014
                    }
                }

                result.LastUsed = DateTime.Now;
            }

            await result.GenerateAsync();
            return result;
        }
    }
}
