using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("neoscrypt")]
public unsafe class NeoScrypt : IHashAlgorithm
{
    public NeoScrypt(uint profile)
    {
        this.profile = profile;
    }

    private readonly uint profile;

    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(data.Length == 80, $"{nameof(data)} length must be exactly 80 bytes");
        Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                Multihash.neoscrypt(input, output, (uint) data.Length, profile);
            }
        }
    }
}
