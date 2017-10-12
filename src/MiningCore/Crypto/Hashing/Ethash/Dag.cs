using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MiningCore.Blockchain.Ethereum;
using MiningCore.Contracts;
using MiningCore.Native;
using NLog;

namespace MiningCore.Crypto.Hashing.Ethash
{
    public class Dag : IDisposable
    {
        public Dag(ulong epoch)
        {
            Epoch = epoch;
        }

        public ulong Epoch { get; set; }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private IntPtr handle = IntPtr.Zero;
        private bool isGenerated = false;
        private readonly object genLock = new object();

        public DateTime LastUsed { get; set; }

        public static unsafe string GetDefaultDagDirectory()
        {
            var chars = new byte[512];

            fixed (byte* data = chars)
            {
                if (LibMultihash.ethash_get_default_dirname(data, chars.Length))
                {
                    int length;
                    for (length = 0; length < chars.Length; length++)
                    {
                        if (data[length] == 0)
                            break;
                    }

                    return Encoding.UTF8.GetString(data, length);
                }
            }

            return null;
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                LibMultihash.ethash_full_delete(handle);
                handle = IntPtr.Zero;
            }
        }

        public async Task GenerateAsync(string dagDir)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(dagDir), $"{nameof(dagDir)} must not be empty");

            await Task.Run(() =>
            {
                lock (genLock)
                {
                    if (!isGenerated)
                    {
                        logger.Info(() => $"Generating DAG for epoch {Epoch}");

                        var started = DateTime.Now;
                        var block = Epoch * EthereumConstants.EpochLength;

                        // Generate a temporary cache
                        var light = LibMultihash.ethash_light_new(block);

                        try
                        {
                            // Generate the actual DAG
                            handle = LibMultihash.ethash_full_new(dagDir, light, progress =>
                            {
                                logger.Info(() => $"Generating DAG: {progress}%");
                                return 0;
                            });

                            if(handle == IntPtr.Zero)
                                throw new OutOfMemoryException("ethash_full_new IO or memory error");

                            logger.Info(() => $"Done generating DAG for epoch {Epoch} after {DateTime.Now - started}");
                            isGenerated = true;
                        }

                        finally
                        {
                            if(light != IntPtr.Zero)
                                LibMultihash.ethash_light_delete(light);
                        }
                    }
                }
            });
        }

        public bool Compute(byte[] hash, ulong nonce, out byte[] mixDigest, out byte[] result)
        {
            Contract.RequiresNonNull(hash, nameof(hash));

            mixDigest = null;
            result = null;

            LibMultihash.ethash_return_value value;
            LibMultihash.ethash_full_compute(handle, hash, nonce, out value);

            if (value.success)
            {
                mixDigest = value.mix_hash.value;
                result = value.result.value;
            }

            return value.success;
        }
    }
}
