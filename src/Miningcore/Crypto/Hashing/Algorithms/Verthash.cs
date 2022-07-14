using System.Diagnostics;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Native;
using Miningcore.Notifications.Messages;
using NLog;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("verthash")]
public unsafe class Verthash :
    IHashAlgorithm,
    IHashAlgorithmInit
{
    internal static IMessageBus messageBus;

    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(data.Length == 80);
        Contract.Requires<ArgumentException>(result.Length >= 32);

        var sw = Stopwatch.StartNew();

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                Multihash.verthash(input, output, data.Length);
            }
        }

        messageBus?.SendTelemetry("Verthash", TelemetryCategory.Hash, sw.Elapsed);
    }

    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

    public bool DigestInit(PoolConfig poolConfig)
    {
        var vertHashDataFile = "verthash.dat";

        if(poolConfig.Extra.TryGetValue("vertHashDataFile", out var result))
            vertHashDataFile = ((string) result).Trim();

        logger.Info(()=> $"Loading verthash data file {vertHashDataFile}");

        return Multihash.verthash_init(vertHashDataFile, false) == 0;
    }
}
