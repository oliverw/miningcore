using Miningcore.Blockchain.Ethereum;
using Miningcore.Contracts;
using Miningcore.Native;
using NLog;

namespace Miningcore.Crypto.Hashing.Ethash;

public class Cache : IDisposable
{
    public Cache(ulong epoch)
    {
        Epoch = epoch;
        LastUsed = DateTime.Now;
    }

    private IntPtr handle = IntPtr.Zero;
    private bool isGenerated = false;
    private readonly object genLock = new();

    public ulong Epoch { get; }
    public DateTime LastUsed { get; set; }

    public void Dispose()
    {
        if(handle != IntPtr.Zero)
        {
            EthHash.ethash_light_delete(handle);
            handle = IntPtr.Zero;
        }
    }

    public async Task GenerateAsync(ILogger logger)
    {
        await Task.Run(() =>
        {
            lock(genLock)
            {
                if(!isGenerated)
                {
                    var started = DateTime.Now;
                    logger.Debug(() => $"Generating cache for epoch {Epoch}");

                    var block = Epoch * EthereumConstants.EpochLength;
                    handle = EthHash.ethash_light_new(block);

                    logger.Debug(() => $"Done generating cache for epoch {Epoch} after {DateTime.Now - started}");
                    isGenerated = true;
                }
            }
        });
    }

    public unsafe bool Compute(ILogger logger, byte[] hash, ulong nonce, out byte[] mixDigest, out byte[] result)
    {
        Contract.RequiresNonNull(hash);

        mixDigest = null;
        result = null;

        var value = new EthHash.ethash_return_value();

        fixed (byte* input = hash)
        {
            EthHash.ethash_light_compute(handle, input, nonce, ref value);
        }

        if(value.success)
        {
            mixDigest = value.mix_hash.value;
            result = value.result.value;
        }

        return value.success;
    }
}
