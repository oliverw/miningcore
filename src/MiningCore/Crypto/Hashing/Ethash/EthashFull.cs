using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MiningCore.Blockchain.Ethereum;
using MiningCore.Contracts;
using NLog;

namespace MiningCore.Crypto.Hashing.Ethash
{
    public class EthashFull : IDisposable
    {
        public EthashFull(int numCaches, string dagDir)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(dagDir), $"{nameof(dagDir)} must not be empty");

            this.numCaches = numCaches;
            this.dagDir = dagDir;
        }

        private int numCaches; // Maximum number of caches to keep before eviction (only init, don't modify)
        private readonly object cacheLock = new object();
        private readonly Dictionary<ulong, Dag> caches = new Dictionary<ulong, Dag>();
        private Dag future;
        private readonly string dagDir;

        public void Dispose()
        {
            foreach(var value in caches.Values)
                value.Dispose();
        }

        public async Task<Dag> GetDagAsync(ulong block, ILogger logger)
        {
            var epoch = block / EthereumConstants.EpochLength;
            Dag result;

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

                        logger.Info(() => $"Evicting DAG for epoch {epochToEvict} in favour of epoch {epoch}");
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
                        logger.Info(() => $"No pre-generated DAG available, creating new for epoch {epoch}");
                        result = new Dag(epoch);
                    }

                    caches[epoch] = result;
                }

                else
                {
                    // If we used up the future cache, or need a refresh, regenerate
                    if (future == null || future.Epoch <= epoch)
                    {
                        logger.Info(() => $"Pre-generating DAG for epoch {epoch + 1}");
                        future = new Dag(epoch + 1);

                        #pragma warning disable 4014
                        future.GenerateAsync(dagDir, logger);
                        #pragma warning restore 4014
                    }
                }

                result.LastUsed = DateTime.Now;
            }

            await result.GenerateAsync(dagDir, logger);
            return result;
        }
    }
}
