using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("ghostrider")]
public class Ghostrider : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        CryptonightBindings.Cryptonight(data, result, CryptonightBindings.Algorithm.GHOSTRIDER_RTM, 0);
    }
}
