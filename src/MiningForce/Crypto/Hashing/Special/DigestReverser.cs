using System.Linq;

namespace MiningForce.Crypto.Hashing.Special
{
	public class DigestReverser : IHashAlgorithm
	{
		public DigestReverser(IHashAlgorithm upstream)
		{
			this.upstream = upstream;
		}

		private readonly IHashAlgorithm upstream;

		public byte[] Transform(byte[] data, object args)
		{
			return upstream.Transform(data, args)
				.Reverse()
				.ToArray();
		}
	}
}
