using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Crypto.Hashing.Ethash
{
    public class LightClient : IDisposable
    {
        public LightClient(int numCaches)
        {
            this.numCaches = numCaches;
        }

        public bool Test { get; set; } // If set, use a smaller cache size

        private int numCaches;  // Maximum number of caches to keep before eviction (only init, don't modify)
        private Dictionary<ulong, Cache> caches = new Dictionary<ulong, Cache>();
        private Cache future;

        public void Dispose()
        {
        }
    }
}
