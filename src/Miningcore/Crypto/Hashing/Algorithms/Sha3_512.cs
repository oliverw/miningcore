using System.Security.Cryptography;
using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("sha3-512")]
public unsafe class Sha3_512 : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 64);

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                Multihash.sha3_512(input, output, (uint) data.Length);
            }
        }
    }
}
