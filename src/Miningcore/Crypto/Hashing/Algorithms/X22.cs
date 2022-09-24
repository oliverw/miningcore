using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("x22")]
public unsafe class X22 : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                Multihash.x22(input, output, (uint) data.Length);
            }
        }
    }
}
