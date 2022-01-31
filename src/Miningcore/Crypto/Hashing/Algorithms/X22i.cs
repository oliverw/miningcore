using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("x22i")]
public unsafe class X22I : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                Multihash.x22i(input, output, (uint) data.Length);
            }
        }
    }
}
