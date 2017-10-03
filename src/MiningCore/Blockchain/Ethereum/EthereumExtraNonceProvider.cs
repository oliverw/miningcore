using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using MiningCore.Extensions;

namespace MiningCore.Blockchain.Ethereum
{
    public class EthereumExtraNonceProvider
    {
        private int counter;
        private const int NonceMax = 0x1000000; // 3 Byte = 24 Bit

        public string Next()
        {
            Interlocked.Increment(ref counter);
            Interlocked.CompareExchange(ref counter, 0, NonceMax);

            // encode to hex
            var result = BitConverter.GetBytes(counter).Take(3).ToReverseArray().ToHexString();
            return result;
        }
    }
}
