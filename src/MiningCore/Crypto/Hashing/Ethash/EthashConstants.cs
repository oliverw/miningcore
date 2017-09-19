using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Crypto.Hashing.Ethash
{
    public class EthashConstants
    {
        public const ulong EpochLength = 30000;

        public const ulong CacheSizeForTesting = 1024;
        public const ulong DagSizeForTesting = 1024 * 32;
    }
}
