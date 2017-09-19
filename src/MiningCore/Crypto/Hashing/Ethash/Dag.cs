using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Crypto.Hashing.Ethash
{
    public class Dag : IDisposable
    {
        public ulong Epoch { get; set; }
        public bool Test { get; set; } // If set, use a smaller cache size
        public string Path { get; set; }

        private FullClient fullClient;

        public void Dispose()
        {
            fullClient?.Dispose();
        }
    }
}
