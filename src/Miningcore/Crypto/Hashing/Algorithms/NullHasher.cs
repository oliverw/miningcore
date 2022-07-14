namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("null")]
public class Null : IHashAlgorithm
{
    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        throw new InvalidOperationException("Don't call me");
    }
}
