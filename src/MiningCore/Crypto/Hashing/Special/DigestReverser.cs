using System.Linq;

namespace MiningCore.Crypto.Hashing.Special
{
    public class DigestReverser : IHashAlgorithm
    {
        private readonly IHashAlgorithm upstream;

        public DigestReverser(IHashAlgorithm upstream)
        {
            this.upstream = upstream;
        }

        public byte[] Digest(byte[] data, ulong nTime)
        {
            return upstream.Digest(data, nTime)
                .Reverse()
                .ToArray();
        }
    }
}
