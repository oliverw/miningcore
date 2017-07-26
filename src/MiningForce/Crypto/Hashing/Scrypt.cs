using MH = MiningForce.Crypto.Hashing.LibMultiHash;

namespace MiningForce.Crypto.Hashing
{
    public unsafe class Scrypt : IHashAlgorithm
    {
	    public class ScryptArgs
	    {
		    public uint? N { get; set; }
		    public uint? R { get; set; }
	    }

		public byte[] Digest(byte[] data, object args)
	    {
		    var result = new byte[32];
		    uint n = 1024, r = 1;
		    var scryptArgs = args as ScryptArgs;

			if (scryptArgs != null)
			{
				if (scryptArgs.N.HasValue)
					n = scryptArgs.N.Value;

				if (scryptArgs.R.HasValue)
					r = scryptArgs.R.Value;
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
