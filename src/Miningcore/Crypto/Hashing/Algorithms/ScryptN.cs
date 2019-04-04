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
using Miningcore.Native;
using Miningcore.Time;

namespace Miningcore.Crypto.Hashing.Algorithms
{
    public unsafe class ScryptN : IHashAlgorithm
    {
        public ScryptN(Tuple<long, long>[] timetable = null)
        {
            this.timetable = timetable ?? defaultTimetable;
        }

        private readonly Tuple<long, long>[] timetable;

        public IMasterClock Clock { get; set; }

        private static readonly Tuple<long, long>[] defaultTimetable = new[]
        {
            Tuple.Create(2048L, 1389306217L),
            Tuple.Create(4096L, 1456415081L),
            Tuple.Create(8192L, 1506746729L),
            Tuple.Create(16384L, 1557078377L),
            Tuple.Create(32768L, 1657741673L),
            Tuple.Create(65536L, 1859068265L),
            Tuple.Create(131072L, 2060394857L),
            Tuple.Create(262144L, 1722307603L),
            Tuple.Create(524288L, 1769642992L),
        }.OrderByDescending(x => x.Item1).ToArray();

        public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
        {
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            // get nFactor
            var ts = ((DateTimeOffset) Clock.Now).ToUnixTimeSeconds();
            var n = timetable.First(x => ts >= x.Item2).Item1;
            var nFactor = Math.Log(n) / Math.Log(2);

            fixed (byte* input = data)
            {
                fixed (byte* output = result)
                {
                    LibMultihash.scryptn(input, output, (uint) nFactor, (uint) data.Length);
                }
            }
        }
    }
}
