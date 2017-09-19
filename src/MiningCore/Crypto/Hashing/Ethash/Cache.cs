using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using MiningCore.Crypto.Hashing.Algorithms;
using MiningCore.Extensions;
using MiningCore.Native;
using NLog;

namespace MiningCore.Crypto.Hashing.Ethash
{
    public class Cache : IDisposable
    {
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private LightHandler lightHandler;

        public async Task GenerateAsync(ulong block)
        {
            await Task.Run(() =>
            {
                var epoch = block / EthashConstants.EpochLength;
                var started = DateTime.Now;

                logger.Debug(() => $"Generating cache for epoch {epoch}");

                lightHandler = new LightHandler(block, 1);

                logger.Debug(() => $"Done generating cache for epoch {epoch} after {DateTime.Now - started}");
            });
        }

        //public bool Compute(ulong dagSize, string hash, ulong nonce, out byte[] mixDigest, out byte[] result)
        //{
        //}

        public void Dispose()
        {
            lightHandler?.Dispose();
        }

        byte[] MakeSeedHashBlock(ulong block)
        {
            return MakeSeedHashEpoch(block / EthashConstants.EpochLength);
        }

        byte[] MakeSeedHashEpoch(ulong epoch)
        {
            var result = new byte[32];
            var hasher = new Sha3_256();

            for(var i = 0ul; i < epoch; i++)
            {
                result = hasher.Digest(result);
            }

            return result;
        }
    }
}
