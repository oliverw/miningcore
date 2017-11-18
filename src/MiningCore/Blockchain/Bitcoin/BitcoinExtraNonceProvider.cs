/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Security.Cryptography;
using System.Threading;

namespace MiningCore.Blockchain.Bitcoin
{
    public class BitcoinExtraNonceProvider : IExtraNonceProvider
    {
        public BitcoinExtraNonceProvider()
        {
            int instanceId;

            using(var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[4];
                rng.GetNonZeroBytes(bytes);
                instanceId = BitConverter.ToInt32(bytes, 0);
            }

            var mask = (1 << (ExtranonceBytes * 8)) - 1;
            counter = Math.Abs(instanceId & mask);
        }

        private int counter;
        public const int ExtranonceBytes = 3; // 3 Byte = 24 Bit
        public const int PlaceHolderLength = 8;
        private const int NonceMax = 1 << (ExtranonceBytes * 8);
        private readonly string stringFormat = "x" + ExtranonceBytes * 2;
        public const int Size = PlaceHolderLength - ExtranonceBytes;

        #region IExtraNonceProvider

        public string Next()
        {
            Interlocked.Increment(ref counter);
            Interlocked.CompareExchange(ref counter, 0, NonceMax);

            // encode to hex
            var result = counter.ToString(stringFormat);
            return result;
        }

        #endregion // IExtraNonceProvider
    }
}
