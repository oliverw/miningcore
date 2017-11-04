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
using System.Threading;
using MiningCore.Contracts;
using MiningCore.Native;

namespace MiningCore.Crypto.Hashing.Equihash
{
    public unsafe class EquihashVerifier
    {
        public EquihashVerifier(int maxConcurrency = 2)
        {
            // need to limit concurrency here due to the enormous memory 
            // requirements of the equihash algorithm (up 1GB per thread)
            sem = new Semaphore(maxConcurrency, maxConcurrency);
        }

        private readonly Semaphore sem;

        /// <summary>
        /// Verify an Equihash solution
        /// </summary>
        /// <param name="header">header including nonce (140 bytes)</param>
        /// <param name="solution">equihash solution (excluding 3 bytes with size, so 1344 bytes length) - Do not include byte size preamble "fd4005"</param>
        /// <returns></returns>
        public bool Verify(byte[] header, byte[] solution)
        {
            Contract.RequiresNonNull(header, nameof(header));
            Contract.Requires<ArgumentException>(header.Length == 140, $"{nameof(header)} must be exactly 140 bytes");
            Contract.RequiresNonNull(solution, nameof(solution));
            Contract.Requires<ArgumentException>(solution.Length == 1344, $"{nameof(solution)} must be exactly 1344 bytes");

            try
            {
                sem.WaitOne();

                fixed(byte* _header = header)
                {
                    fixed(byte* _solution = solution)
                    {
                        return LibMultihash.equihash_verify(_header, _solution);
                    }
                }
            }

            finally
            {
                sem.Release();
            }
        }
    }
}
