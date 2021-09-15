using System;
using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms
{
    public unsafe class Skein : IHashAlgorithm
    {
        public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
        {
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            fixed (byte* input = data)
            {
                fixed (byte* output = result)
                {
                    LibMultihash.skein(input, output, (uint) data.Length);
                }
            }
        }
    }
}
