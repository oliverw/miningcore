using MH = MiningForce.Crypto.Hashing.LibMultiHash;

namespace MiningForce.Crypto.Hashing.Algorithms
{
    public unsafe class Blake : IHashAlgorithm
    {
		public byte[] Digest(byte[] data, object args)
	    {
		    var result = new byte[32];

			fixed (byte* input = data)
		    {
			    fixed (byte* output = result)
			    {
				    MH.blake(input, output, (uint) data.Length);
			    }
			}

		    return result;
	    }
    }
}
