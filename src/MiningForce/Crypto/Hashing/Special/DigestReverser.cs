using System.Linq;
using MiningForce.Extensions;

namespace MiningForce.Crypto.Hashing.Special
{
	public class DigestReverser : IHashAlgorithm
	{
		public DigestReverser(IHashAlgorithm upstream)
		{
			this.upstream = upstream;
		}

		private readonly IHashAlgorithm upstream;

		public byte[] Digest(byte[] data, object args)
		{
			return upstream.Digest(data, args).ToReverseArray();
		}
	}
}
