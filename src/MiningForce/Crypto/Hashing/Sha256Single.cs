using System.Security.Cryptography;

namespace MiningForce.Crypto.Hashing
{
    public class Sha256Single : IHashAlgorithm
    {
	    public byte[] Digest(byte[] data, object args)
	    {
		    using (var hasher = SHA256.Create())
		    {
			    return hasher.ComputeHash(data);
		    }
	    }
    }
}
