using MiningCore.Native;

namespace MiningCore.Crypto.Hashing.Algorithms
{
    public unsafe class Blake : IHashAlgorithm
    {
        public byte[] Digest(byte[] data, ulong nTime)
        {
            var result = new byte[32];

            fixed (byte* input = data)
            {
                fixed (byte* output = result)
                {
                    LibMultihash.blake(input, output, (uint) data.Length);
                }
            }

            return result;
        }
    }
}