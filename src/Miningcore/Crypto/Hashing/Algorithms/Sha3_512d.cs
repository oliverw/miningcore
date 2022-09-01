using System.Security.Cryptography;
using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("sha3-512d")]
public unsafe class Sha3_512d : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 64);

        Span<byte> tmp = stackalloc byte[64];

        fixed(byte* input = data)
        {
            fixed(byte* _tmp = tmp)
            {
                fixed(byte* output = result)
                {
                    Multihash.sha3_512(input, _tmp, (uint) data.Length);
                    Multihash.sha3_512(_tmp, output, 64);
                }
            }
        }
    }
}
