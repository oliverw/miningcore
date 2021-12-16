using System.Security.Cryptography;
using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

/// <summary>
/// Sha-256 single round
/// </summary>
public unsafe class Sha256Csm : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                LibMultihash.sha256csm(input, output, (uint) data.Length);
            }
        }
    }
}
