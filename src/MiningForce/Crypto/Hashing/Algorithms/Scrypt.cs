using MiningForce.Native;

namespace MiningForce.Crypto.Hashing.Algorithms
{
    public unsafe class Scrypt : IHashAlgorithm
    {
		public Scrypt(uint n, uint r)
		{
			this.n = n;
			this.r = r;
		}

	    private readonly uint n;
	    private readonly uint r;

		public byte[] Digest(byte[] data, ulong nTime)
	    {
		    var result = new byte[32];

			fixed (byte* input = data)
		    {
			    fixed (byte* output = result)
			    {
				    libmultihash.scrypt(input, output, n, r, (uint) data.Length);
			    }
			}

		    return result;
	    }
    }
}
