using MH = MiningForce.Crypto.Hashing.LibMultiHash;

namespace MiningForce.Crypto.Hashing
{
    public unsafe class Scrypt : IHashAlgorithm
    {
		public byte[] Digest(byte[] data, object args)
	    {
		    var result = new byte[32];

		    uint n = 1024, r = 1;

			if (args != null)
		    {
		    }

		    fixed (byte* input = data)
		    {
			    fixed (byte* output = result)
			    {
				    MH.scrypt(input, output, n, r, (uint) data.Length);
			    }
			}

		    return result;
	    }
    }
}
