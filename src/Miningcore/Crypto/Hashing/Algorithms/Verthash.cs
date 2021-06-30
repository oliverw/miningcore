using System;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Native;
using NLog;

namespace Miningcore.Crypto.Hashing.Algorithms
{
    public unsafe class Verthash :
        IHashAlgorithm,
        IHashAlgorithmInit
    {
        public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
        {
            Contract.Requires<ArgumentException>(data.Length == 80, $"{nameof(data)} must be exactly 80 bytes long");
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            fixed (byte* input = data)
            {
                fixed (byte* output = result)
                {
                    LibMultihash.verthash(input, output, data.Length);
                }
            }
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public bool DigestInit(PoolConfig poolConfig)
        {
            var vertHashDataFile = "verthash.dat";

            if(poolConfig.Extra.TryGetValue("vertHashDataFile", out var result))
                vertHashDataFile = ((string) result).Trim();

            logger.Info(()=> $"Loading verthash data file {vertHashDataFile}");

            return LibMultihash.verthash_init(vertHashDataFile, false) == 0;
        }
    }
}
