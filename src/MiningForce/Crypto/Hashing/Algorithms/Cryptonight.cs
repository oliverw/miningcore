using MH = MiningForce.Crypto.Hashing.LibMultiHash;

namespace MiningForce.Crypto.Hashing.Algorithms
{
    public unsafe class Cryptonight : IHashAlgorithm
    {
		public byte[] Digest(byte[] data, ulong nTime)
	    {
		    var result = new byte[32];

			fixed (byte* input = data)
		    {
			    fixed (byte* output = result)
			    {
				    MH.cryptonight(input, output, (uint) data.Length, false);
			    }
			}

		    return result;
	    }
    }
}
