using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("x21s")]
public unsafe class X21S : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                Multihash.x21s(input, output, (uint) data.Length);
            }
        }
    }
}
