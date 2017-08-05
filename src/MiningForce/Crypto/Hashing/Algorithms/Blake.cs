using MiningForce.Native;

namespace MiningForce.Crypto.Hashing.Algorithms
{
    public unsafe class Blake : IHashAlgorithm
    {
		public byte[] Digest(byte[] data, ulong nTime)
	    {
		    var result = new byte[32];

			fixed (byte* input = data)
		    {
			    fixed (byte* output = result)
			    {
				    libmultihash.blake(input, output, (uint) data.Length);
			    }
			}

		    return result;
	    }
    }
}
