using System.Diagnostics;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Native;
using Miningcore.Notifications.Messages;
using NLog;

namespace Miningcore.Crypto.Hashing.Ethash.Etchash;

[Identifier("etchash")]
public class Cache : IEthashCache
{
    public Cache(ulong epoch)
    {
        Epoch = epoch;
        LastUsed = DateTime.Now;
    }

    private IntPtr handle = IntPtr.Zero;
    private bool isGenerated = false;
    private readonly object genLock = new();
    internal static IMessageBus messageBus;
    private ulong hardForkBlock;

    public ulong Epoch { get; }
    public DateTime LastUsed { get; set; }

    public void Dispose()
    {
        if(handle != IntPtr.Zero)
        {
            EtcHash.ethash_light_delete(handle);
            handle = IntPtr.Zero;
        }
    }

    public async Task GenerateAsync(ILogger logger, ulong epochLength, ulong hardForkBlock)
    {
        await Task.Run(() =>
        {
            lock(genLock)
            {
                if(!isGenerated)
                {
                    this.hardForkBlock = hardForkBlock;

                    var started = DateTime.Now;
                    logger.Debug(() => $"Generating cache for epoch {Epoch}");

                    var block = Epoch * epochLength;
                    handle = EtcHash.ethash_light_new(block, hardForkBlock);

                    logger.Debug(() => $"Done generating cache for epoch {Epoch} after {DateTime.Now - started}");
                    isGenerated = true;
                }
            }
        });
    }

    public unsafe bool Compute(ILogger logger, byte[] hash, ulong nonce, out byte[] mixDigest, out byte[] result)
    {
        Contract.RequiresNonNull(hash);

        var sw = Stopwatch.StartNew();

        mixDigest = null;
        result = null;

        var value = new EtcHash.ethash_return_value();

        fixed(byte* input = hash)
        {
            EtcHash.ethash_light_compute(handle, input, nonce, this.hardForkBlock, ref value);
        }

        if(value.success)
        {
            mixDigest = value.mix_hash.value;
            result = value.result.value;
        }

        messageBus?.SendTelemetry("Etchash", TelemetryCategory.Hash, sw.Elapsed, value.success);

        return value.success;
    }
}