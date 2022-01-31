using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("scrypt")]
public unsafe class Scrypt : IHashAlgorithm
{
    public Scrypt(uint n, uint r)
    {
        this.n = n;
        this.r = r;
    }

    private readonly uint n;
    private readonly uint r;

    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                Multihash.scrypt(input, output, n, r, (uint) data.Length);
            }
        }
    }
}
