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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MiningCore.Extensions;

namespace MiningCore.Blockchain.Bitcoin
{
    public class BitcoinExtraNonceProvider
    {
        public BitcoinExtraNonceProvider()
        {
            uint instanceId;

            using (var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[4];
                rng.GetNonZeroBytes(bytes);
                instanceId = BitConverter.ToUInt32(bytes, 0);
            }

            counter = instanceId << 27;
        }

        private uint counter;

        public byte[] PlaceHolder { get; } = "f000000ff111111f".HexToByteArray();

        public int Size => PlaceHolder.Length - Marshal.SizeOf(counter);

        public uint Next()
        {
            return ++counter;
        }
    }
}
