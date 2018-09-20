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
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using MiningCore.Buffers;
using MiningCore.Contracts;

namespace MiningCore.Native
{
    public static unsafe class LibCryptonight
    {
        [DllImport("libcryptonight", EntryPoint = "cryptonight_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern int cryptonight(byte* input, byte* output, uint inputLength, int variant);

        [DllImport("libcryptonight", EntryPoint = "cryptonight_light_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern int cryptonight_light(byte* input, byte* output, uint inputLength, int variant);

        [DllImport("libcryptonight", EntryPoint = "cryptonight_heavy_export", CallingConvention = CallingConvention.Cdecl)]
        private static extern int cryptonight_heavy(byte* input, byte* output, uint inputLength, int variant);

        public static PooledArraySegment<byte> Cryptonight(byte[] data, int variant)
        {
            Contract.RequiresNonNull(data, nameof(data));

            var result = new PooledArraySegment<byte>(32);

            fixed(byte* input = data)
            {
                fixed(byte* output = result.Array)
                {
                    cryptonight(input, output, (uint) data.Length, variant);
                }
            }

            return result;
        }

        public static PooledArraySegment<byte> CryptonightLight(byte[] data, int variant)
        {
            Contract.RequiresNonNull(data, nameof(data));

            var result = new PooledArraySegment<byte>(32);

            fixed (byte* input = data)
            {
                fixed (byte* output = result.Array)
                {
                    cryptonight_light(input, output, (uint)data.Length, variant);
                }
            }

            return result;
        }

        public static PooledArraySegment<byte> CryptonightHeavy(byte[] data, int variant)
        {
            Contract.RequiresNonNull(data, nameof(data));

            var result = new PooledArraySegment<byte>(32);

            fixed (byte* input = data)
            {
                fixed (byte* output = result.Array)
                {
                    cryptonight_heavy(input, output, (uint)data.Length, variant);
                }
            }

            return result;
        }
    }
}
