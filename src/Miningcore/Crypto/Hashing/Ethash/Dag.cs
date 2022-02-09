using System.Diagnostics;
using System.Text;
using Miningcore.Blockchain.Ethereum;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Native;
using Miningcore.Notifications.Messages;
using NLog;

namespace Miningcore.Crypto.Hashing.Ethash;

public class Dag : IDisposable
{
    public Dag(ulong epoch)
    {
        Epoch = epoch;
    }

    public ulong Epoch { get; set; }

    private IntPtr handle = IntPtr.Zero;
    private static readonly Semaphore sem = new(1, 1);

    internal static IMessageBus messageBus;

    public DateTime LastUsed { get; set; }

    private const int DefaultLockTimeoutInMinutes = 40;

    public static unsafe string GetDefaultDagDirectory()
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
            EthHash.ethash_full_delete(handle);
            handle = IntPtr.Zero;
        }
    }

    public async ValueTask GenerateAsync(string dagDir, int numCaches, ILogger logger, CancellationToken ct)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(dagDir), $"{nameof(dagDir)} must not be empty");
        var timeout = TimeSpan.FromMinutes(DefaultLockTimeoutInMinutes);

        if(handle == IntPtr.Zero)
        {
            await Task.Run(() =>
            {
                if (sem.WaitOne(timeout))
                {
                    try
                    {
                        // re-check after obtaining lock
                        if(handle != IntPtr.Zero)
                            return;

                        logger.Info(() => $"Generating DAG for epoch {Epoch}");

                        var started = DateTime.Now;
                        var block = Epoch * EthereumConstants.EpochLength;

                        // Generate a temporary cache
                        var light = EthHash.ethash_light_new(block);

                        try
                        {
                            // Generate the actual DAG
                            handle = EthHash.ethash_full_new(dagDir, light, progress =>
                            {
                                logger.Info(() => $"Generating DAG for epoch {Epoch}: {progress}%");

                                return !ct.IsCancellationRequested ? 0 : 1;
                            });

                            if(handle == IntPtr.Zero)
                                throw new OutOfMemoryException("ethash_full_new IO or memory error");

                            logger.Info(() => $"Done generating DAG for epoch {Epoch} after {DateTime.Now - started}");
                        }

                        finally
                        {
                            if(light != IntPtr.Zero)
                                EthHash.ethash_light_delete(light);

                            CleanupOldDags(dagDir, numCaches, logger);
                        }
                    }

                    finally
                    {
                        sem.Release();
                    }
                }
                else
                {
                    logger.Info(() => $"The dag lock was not acquired. Timeout after {timeout.TotalMinutes} minutes");
                }
            }, ct);
        }
    }

    public unsafe bool Compute(ILogger logger, byte[] hash, ulong nonce, out byte[] mixDigest, out byte[] result)
    {
        Contract.RequiresNonNull(hash, nameof(hash));

        var sw = Stopwatch.StartNew();

        mixDigest = null;
        result = null;

        var value = new EthHash.ethash_return_value();

        fixed (byte* input = hash)
        {
            EthHash.ethash_full_compute(handle, input, nonce, ref value);
        }

        if(value.success)
        {
            mixDigest = value.mix_hash.value;
            result = value.result.value;
        }

        messageBus?.SendTelemetry("Ethash", TelemetryCategory.Hash, sw.Elapsed, value.success);

        return value.success;
    }

    private void CleanupOldDags(string dagDir, int numCaches, ILogger logger)
    {
        // Cleanup old Dag files asynchronously
        _ = Task.Run(() =>
        {
            try
            {
                logger.Debug($"Cleaning up old DAG files in {dagDir}");

                var dagFiles = Directory.GetFiles(dagDir);

                if(dagFiles.Length > numCaches)
                {
                    logger.Debug($"There are {dagFiles.Length} DAG files, and {dagFiles.Length - numCaches} will be deleted");

                    // Comparing f2 to f1 (instead of f1 to f2) will sort the filenames in descending order by creation time
                    // i.e., newer files will be first, older files after
                    Array.Sort(dagFiles, Comparer<string>.Create((f1, f2) => File.GetCreationTimeUtc(f2).CompareTo(File.GetCreationTimeUtc(f1))));

                    for(var i = numCaches; i < dagFiles.Length; i++)
                    {
                        logger.Debug($"Deleting {dagFiles[i]}");
                        File.Delete(dagFiles[i]);
                        logger.Debug($"Deleted {dagFiles[i]}");
                    }
                }

                logger.Info($"Finished cleaning up old DAG files in {dagDir}");
            }
            catch(Exception e)
            {
                logger.Error(() => $"Exception while cleaning up old Dag files in {dagDir}: {e}");
            }
        });
    }
}
