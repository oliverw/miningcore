using System.Security.Cryptography;

namespace MiningCore.Crypto.Hashing.Algorithms
{
    /// <summary>
    /// Sha-256 double round
    /// </summary>
    public class Sha256D : IHashAlgorithm
    {
        public byte[] Digest(byte[] data, ulong nTime)
        {
            using (var hasher = SHA256.Create())
            {
                return hasher.ComputeHash(hasher.ComputeHash(data));
            }
        }
    }
}