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
using System.Linq;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms
{
    public unsafe class Keccak : IHashAlgorithm
    {
        public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
        {
            Contract.RequiresNonNull(extra, nameof(extra));
            Contract.Requires<ArgumentException>(extra.Length > 0, $"{nameof(extra)} must not be empty");
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            // concat nTime as hex string to data
            var nTime = (ulong) extra[0];
            var nTimeHex = nTime.ToString("X").HexToByteArray();

            Span<byte> dataEx = stackalloc byte[data.Length + nTimeHex.Length];
            data.CopyTo(dataEx);

            if(nTimeHex.Length > 0)
            {
                var dest = dataEx.Slice(data.Length);
                nTimeHex.CopyTo(dest);
            }

            fixed (byte* input = dataEx)
            {
                fixed (byte* output = result)
                {
                    LibMultihash.keccak(input, output, (uint) data.Length);
                }
            }
        }
    }
}
