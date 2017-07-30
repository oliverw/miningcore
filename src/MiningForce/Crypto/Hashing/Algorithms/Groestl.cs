using MH = MiningForce.Crypto.Hashing.LibMultiHash;

namespace MiningForce.Crypto.Hashing.Algorithms
{
    public unsafe class Groestl : IHashAlgorithm
    {
		public byte[] Digest(byte[] data, ulong nTime)
	    {
		    var result = new byte[32];

			fixed (byte* input = data)
		    {
			    fixed (byte* output = result)
			    {
				    MH.groestl(input, output, (uint) data.Length);
			    }
			}

		    return result;
	    }
    }
}
