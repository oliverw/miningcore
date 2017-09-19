using System;
using System.Collections.Generic;
using System.Text;
using MiningCore.Native;

namespace MiningCore.Crypto.Hashing.Ethash
{
    public class LightHandler : IDisposable
    {
        public LightHandler(ulong block, int numCaches)
        {
            this.numCaches = numCaches;

            handle = LibMultihash.ethash_light_new(block);
        }

        private IntPtr handle = IntPtr.Zero;
        private int numCaches;  // Maximum number of caches to keep before eviction (only init, don't modify)
        private Dictionary<ulong, Cache> caches = new Dictionary<ulong, Cache>();
        private Cache future;

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                LibMultihash.ethash_light_delete(handle);
                handle = IntPtr.Zero;
            }
        }
    }
}
