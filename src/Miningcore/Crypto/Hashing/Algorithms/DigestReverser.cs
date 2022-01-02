namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("reverse")]
public class DigestReverser : IHashAlgorithm
{
    public DigestReverser(IHashAlgorithm upstream)
    {
        this.upstream = upstream;
    }

    private readonly IHashAlgorithm upstream;

    public IHashAlgorithm Upstream => upstream;

    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        upstream.Digest(data, result, extra);
        result.Reverse();
    }
}
