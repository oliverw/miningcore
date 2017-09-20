using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using MiningCore.Blockchain.Ethereum;
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

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                LibMultihash.ethash_full_delete(handle);
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
                        logger.Debug(() => $"Generating DAG for epoch {Epoch}");

                        var started = DateTime.Now;
                        var block = Epoch * EthereumConstants.EpochLength;

                        // Generate a temporary cache
                        var light = LibMultihash.ethash_light_new(block);

                        try
                        {
                            // Generate the actual DAG
                            handle = LibMultihash.ethash_full_new(light, progress =>
                            {
                                logger.Debug(() => $"Generating DAG: {progress}%");
                                return 0;
                            });

                            if(handle == IntPtr.Zero)
                                throw new OutOfMemoryException("ethash_full_new IO or memory error");

                            logger.Debug(() => $"Done generating DAG for epoch {Epoch} after {DateTime.Now - started}");
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
    }
}
