using System;
using System.Diagnostics;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Native;
using Miningcore.Notifications.Messages;
using NLog;

namespace Miningcore.Crypto.Hashing.Algorithms
{
    public unsafe class Verthash :
        IHashAlgorithm,
        IHashAlgorithmInit
    {
        internal static IMessageBus messageBus;

        public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
        {
            Contract.Requires<ArgumentException>(data.Length == 80, $"{nameof(data)} must be exactly 80 bytes long");
            Contract.Requires<ArgumentException>(result.Length >= 32, $"{nameof(result)} must be greater or equal 32 bytes");

            var sw = Stopwatch.StartNew();

            fixed (byte* input = data)
            {
                fixed (byte* output = result)
                {
                    LibMultihash.verthash(input, output, data.Length);
                }
            }

            messageBus.SendTelemetry("Verthash", TelemetryCategory.Hash, sw.Elapsed);
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
