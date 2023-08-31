using System.Diagnostics;
using System.Text;
using Miningcore.Blockchain.Ethereum;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Native;
using Miningcore.Notifications.Messages;
using NLog;

namespace Miningcore.Crypto.Hashing.Ethash.Ethash;

[Identifier("ethash")]
public class Cache : IEthashCache
{
    public Cache(ulong epoch, string dagDir = null)
    {
        Epoch = epoch;
        this.dagDir = dagDir;
        LastUsed = DateTime.Now;
    }

    private IntPtr handle = IntPtr.Zero;
    private bool isGenerated = false;
    private readonly object genLock = new();
    internal static IMessageBus messageBus;
    public ulong Epoch { get; }
    private string dagDir;
    public DateTime LastUsed { get; set; }
    
    public static unsafe string GetDefaultdagDirectory()
    {
        var chars = new byte[512];

        fixed (byte* data = chars)
        {
            if(EthHash.ethash_get_default_dirname(data, chars.Length))
            {
                int length;
                for(length = 0; length < chars.Length; length++)
                {
                    if(data[length] == 0)
                        break;
                }

                return Encoding.UTF8.GetString(data, length);
            }
        }

        return null;
    }

    public void Dispose()
    {
        if(handle != IntPtr.Zero)
        {
            // Full DAG
            if(!string.IsNullOrEmpty(dagDir))
            {
                EthHash.ethash_full_delete(handle);
            }
            // Light Cache
            else
            {
                EthHash.ethash_light_delete(handle);
            }
            
            handle = IntPtr.Zero;
        }
    }

    public async Task GenerateAsync(ILogger logger, CancellationToken ct)
    {
        if(handle == IntPtr.Zero)
        {
            await Task.Run(() =>
            {
                lock(genLock)
                {
                    if(!isGenerated)
                    {
                        // re-check after obtaining lock
                        if(handle != IntPtr.Zero)
                            return;
                        
                        var started = DateTime.Now;
                        var block = Epoch * EthereumConstants.EpochLength;

                        // Full DAG
                        if(!string.IsNullOrEmpty(dagDir))
                        {
                            logger.Debug(() => $"Generating DAG for epoch {Epoch}");
                            logger.Debug(() => $"Epoch length used: {EthereumConstants.EpochLength}");

                            // Generate a temporary cache
                            var light = EthHash.ethash_light_new(block);

                            // Generate the actual DAG
                            handle = EthHash.ethash_full_new(dagDir, light, progress =>
                            {
                                logger.Info(() => $"Generating DAG for epoch {Epoch}: {progress}%");

                                return !ct.IsCancellationRequested ? 0 : 1;
                            });

                            if(handle == IntPtr.Zero)
                                throw new OutOfMemoryException("ethash_full_new IO or memory error");

                            if(light != IntPtr.Zero)
                                EthHash.ethash_light_delete(light);

                            logger.Info(() => $"Done generating DAG for epoch {Epoch} after {DateTime.Now - started}");
                        }
                        // Light Cache
                        else
                        {
                            logger.Debug(() => $"Generating cache for epoch {Epoch}");

                            handle = EthHash.ethash_light_new(block);

                            logger.Debug(() => $"Done generating cache for epoch {Epoch} after {DateTime.Now - started}");
                        }

                        isGenerated = true;
                    }
                }
            }, ct);
        }
    }

    public unsafe bool Compute(ILogger logger, byte[] hash, ulong nonce, out byte[] mixDigest, out byte[] result)
    {
        Contract.RequiresNonNull(hash);

        var sw = Stopwatch.StartNew();

        mixDigest = null;
        result = null;

        var value = new EthHash.ethash_return_value();
        
        // Full DAG
        if(!string.IsNullOrEmpty(dagDir))
        {
            fixed (byte* input = hash)
            {
                EthHash.ethash_full_compute(handle, input, nonce, ref value);
            }
        }
        // Light Cache
        else
        {
            fixed(byte* input = hash)
            {
                EthHash.ethash_light_compute(handle, input, nonce, ref value);
            }
        }

        if(value.success)
        {
            mixDigest = value.mix_hash.value;
            result = value.result.value;
        }

        messageBus?.SendTelemetry("Ethash", TelemetryCategory.Hash, sw.Elapsed, value.success);

        return value.success;
    }
}
