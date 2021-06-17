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

namespace Miningcore.Blockchain
{
    public class ExtraNonceProviderBase : IExtraNonceProvider
    {
        public ExtraNonceProviderBase(int extranonceBytes, byte? instanceId)
        {
            this.extranonceBytes = extranonceBytes;
            idShift = (extranonceBytes * 8) - IdBits;
            nonceMax = (1UL << idShift) - 1;
            stringFormat = "x" + extranonceBytes * 2;

            // generate instanceId if not provided
            var mask = (1L << IdBits) - 1;

            if(instanceId.HasValue)
                id = instanceId.Value;
            else
            {
                using(var rng = RandomNumberGenerator.Create())
                {
                    var bytes = new byte[1];
                    rng.GetNonZeroBytes(bytes);
                    id = bytes[0];
                }
            }

            id = (byte) (id & mask);
            counter = 0;
        }

        private const int IdBits = 5;
        private readonly object counterLock = new();
        protected ulong counter;
        protected byte id;
        protected readonly int extranonceBytes;
        protected readonly int idShift;
        protected readonly ulong nonceMax;
        protected readonly string stringFormat;

        #region IExtraNonceProvider

        public string Next()
        {
            ulong value;

            lock(counterLock)
            {
                counter++;
                if(counter > nonceMax)
                    counter = 0;

                // encode to hex
                value = ((ulong) id << idShift) | counter;
            }

            return value.ToString(stringFormat);
        }

        #endregion // IExtraNonceProvider
    }
}
