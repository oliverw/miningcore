using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MiningCore.Crypto.Hashing.Ethash
{
    public class Cache : IDisposable
    {
        public ulong Epoch { get; set; }
        public DateTime? Used { get; set; }
        public bool Test { get; set; }  // If set, use a smaller cache size

        private LightClient lightClient;

        public Task GenerateAsync()
        {
            throw new NotImplementedException();
        }

        public bool Compute(ulong dagSize, string hash, ulong nonce)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            lightClient?.Dispose();
        }
    }
}
