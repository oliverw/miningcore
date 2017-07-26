using MH = MiningForce.Crypto.Hashing.LibMultiHash;

namespace MiningForce.Crypto.Hashing.Algorithms
{
    public unsafe class ScryptN : IHashAlgorithm
    {
		public byte[] Digest(byte[] data, object args)
	    {
		    var result = new byte[32];
		    var nFactor = (uint) args;

			fixed (byte* input = data)
		    {
			    fixed (byte* output = result)
			    {
				    MH.scryptn(input, output, nFactor, (uint) data.Length);
			    }
			}

		    return result;
	    }
    }
}
