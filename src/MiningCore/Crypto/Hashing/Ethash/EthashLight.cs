using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using MiningCore.Blockchain.Ethereum;
using MiningCore.Contracts;
using MiningCore.Extensions;
using NLog;

namespace MiningCore.Crypto.Hashing.Ethash
{
    public class EthashLight : IDisposable
    {
        public EthashLight(int numCaches)
        {
            this.numCaches = numCaches;
        }

        private int numCaches; // Maximum number of caches to keep before eviction (only init, don't modify)
        private readonly object cacheLock = new object();
        private readonly Dictionary<ulong, Cache> caches = new Dictionary<ulong, Cache>();
        private Cache future;

        public void Dispose()
        {
            foreach(var value in caches.Values)
                value.Dispose();
        }

        public async Task<bool> VerifyBlockAsync(Block block, ILogger logger)
        {
            Contract.RequiresNonNull(block, nameof(block));

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
            var cache = await GetCacheAsync(block.Height, logger);

            // Recompute the hash using the cache
            if (!cache.Compute(logger, block.HashNoNonce, block.Nonce, out var mixDigest, out var resultBytes))
                return false;

            // avoid mixdigest malleability as it's not included in a block's "hashNononce"
            if (!block.MixDigest.SequenceEqual(mixDigest))
                return false;

            // The actual check.
            var target = BigInteger.Divide(EthereumConstants.BigMaxValue, block.Difficulty);
            var resultValue = new BigInteger(resultBytes.ReverseArray());
            var result = resultValue.CompareTo(target) <= 0;
            return result;
        }

        private async Task<Cache> GetCacheAsync(ulong block, ILogger logger)
        {
            var epoch = block / EthereumConstants.EpochLength;
            Cache result;

            lock(cacheLock)
            {
                if (numCaches == 0)
                    numCaches = 3;

                if (!caches.TryGetValue(epoch, out result))
                {
                    // No cached DAG, evict the oldest if the cache limit was reached
                    while(caches.Count >= numCaches)
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
                        future.GenerateAsync(logger);
#pragma warning restore 4014
                    }
                }

                result.LastUsed = DateTime.Now;
            }

            await result.GenerateAsync(logger);
            return result;
        }
    }
}
