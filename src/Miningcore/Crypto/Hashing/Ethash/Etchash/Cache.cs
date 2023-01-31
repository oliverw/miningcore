using Miningcore.Contracts;
using Miningcore.Native;
using NLog;

namespace Miningcore.Crypto.Hashing.Ethash.Etchash;

public class Cache : IDisposable
{
    public Cache(ulong epoch, ulong hardForkBlock)
    {
        Epoch = epoch;
        LastUsed = DateTime.Now;
        this.hardForkBlock = hardForkBlock;
    }

    private IntPtr handle = IntPtr.Zero;
    private bool isGenerated = false;
    private readonly object genLock = new();
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

    public async Task GenerateAsync(ILogger logger, ulong dagEpochLength)
    {
        await Task.Run(() =>
        {
            lock(genLock)
            {
                if(!isGenerated)
                {
                    var started = DateTime.Now;
                    logger.Debug(() => $"Generating cache for epoch {Epoch}");

                    var block = Epoch * dagEpochLength;

                    logger.Info(() => $"Epoch length used: {dagEpochLength}");
                    logger.Info(() => $"Hard fork block used: {hardForkBlock}");
                    logger.Info(() => $"Block used: {block}");

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

        mixDigest = null;
        result = null;

        var value = new EtcHash.ethash_return_value();

        fixed(byte* input = hash)
        {
            EtcHash.ethash_light_compute(handle, input, nonce, this.hardForkBlock, ref value);
        }

        logger.Info(() => $"ethash_light_compute: {value.success}");
        logger.Info(() => $"ethash_light_compute hash: {value.mix_hash}");

        if(value.success)
        {
            mixDigest = value.mix_hash.value;
            result = value.result.value;
        }

        return value.success;
    }
}