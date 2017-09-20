using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace MiningCore.Crypto.Hashing.Ethash
{
    public class Block
    {
        public ulong Height { get; set; }
        public BigInteger Difficulty { get; set; }
        public byte[] HashNoNonce { get; set; }
        public ulong Nonce { get; set; }
        public byte[] MixDigest { get; set; }
    }
}
