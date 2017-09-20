using System;
using System.Threading.Tasks;
using MiningCore.Blockchain.Ethereum;
using MiningCore.Contracts;
using MiningCore.Native;
using NLog;

namespace MiningCore.Crypto.Hashing.Ethash
{
    public class Cache : IDisposable
    {
        public Cache(ulong epoch)
        {
            Epoch = epoch;
            LastUsed = DateTime.Now;
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private IntPtr handle = IntPtr.Zero;
        private bool isGenerated = false;
        private readonly object genLock = new object();

        public ulong Epoch { get; }
        public DateTime LastUsed { get; set; }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                LibMultihash.ethash_light_delete(handle);
                handle = IntPtr.Zero;
            }
        }

        public async Task GenerateAsync()
        {
            await Task.Run(() =>
            {
                lock (genLock)
                {
                    if (!isGenerated)
                    {
                        var started = DateTime.Now;
                        logger.Debug(() => $"Generating cache for epoch {Epoch}");

                        var block = Epoch * EthereumConstants.EpochLength;
                        handle = LibMultihash.ethash_light_new(block);

                        logger.Debug(() => $"Done generating cache for epoch {Epoch} after {DateTime.Now - started}");
                        isGenerated = true;
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
            LibMultihash.ethash_light_compute(handle, hash, nonce, out value);

            if (value.success)
            {
                mixDigest = value.mix_hash.value;
                result = value.result.value;
            }

            return value.success;
        }
    }
}
