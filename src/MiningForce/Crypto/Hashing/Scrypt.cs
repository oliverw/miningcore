using System.Runtime.InteropServices;

namespace MiningForce.Crypto.Hashing
{
    public unsafe class Scrypt : IHashAlgorithm
    {
	    [DllImport("multihash-native", CallingConvention = CallingConvention.Cdecl)]
	    static extern int scrypt(byte *input, byte* output, uint n, uint r, uint len);

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
				    scrypt(input, output, n, r, (uint) data.Length);
			    }
			}

		    return result;
	    }
    }
}
