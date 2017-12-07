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

namespace MiningCore.Blockchain
{
    public class ExtraNonceProviderBase : IExtraNonceProvider
    {
        public ExtraNonceProviderBase(int extranonceBytes)
        {
            this.extranonceBytes = extranonceBytes;
            nonceMax = (1L << (extranonceBytes * 8)) - 1;
            stringFormat = "x" + extranonceBytes * 2;

            uint instanceId;

            using(var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[4];
                rng.GetNonZeroBytes(bytes);
                instanceId = BitConverter.ToUInt32(bytes, 0);
            }

            var mask = (1L << (extranonceBytes * 8)) - 1;
            counter = Math.Abs(instanceId & mask);
        }

        private readonly object counterLock = new object();
        protected long counter;
        protected readonly int extranonceBytes;
        protected readonly long nonceMax;
        protected readonly string stringFormat;

        #region IExtraNonceProvider

        public string Next()
        {
            lock(counterLock)
            {
                counter++;
                if (counter > nonceMax)
                    counter = 0;

                // encode to hex
                var result = counter.ToString(stringFormat);
                return result;
            }
        }

        #endregion // IExtraNonceProvider
    }
}
