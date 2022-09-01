using System.Security.Cryptography;
using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("sha3-256d")]
public unsafe class Sha3_256d : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        Span<byte> tmp = stackalloc byte[32];

        fixed(byte* input = data)
        {
            fixed(byte* _tmp = tmp)
            {
                fixed(byte* output = result)
                {
                    Multihash.sha3_256(input, _tmp, (uint) data.Length);
                    Multihash.sha3_256(_tmp, output, 32);
                }
            }
        }
    }
}
