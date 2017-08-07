using MiningCore.Native;

namespace MiningCore.Crypto.Hashing.Algorithms
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
				    LibMultihash.groestl(input, output, (uint) data.Length);
			    }
			}

		    return result;
	    }
    }
}
