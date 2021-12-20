using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("lyra2-rev3")]
public unsafe class Lyra2Rev3 : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(data.Length == 80, $"{nameof(data)} must be exactly 80 bytes long");
        Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                LibMultihash.lyra2rev3(input, output);
            }
        }
    }
}
