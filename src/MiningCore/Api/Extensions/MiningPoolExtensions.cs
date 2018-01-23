using System.Linq;
using AutoMapper;
using MiningCore.Api.Responses;
using MiningCore.Blockchain;
using MiningCore.Blockchain.Ethereum.Configuration;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Mining;

namespace MiningCore.Api.Extensions
{
    public static class MiningPoolExtensions
    {
        public static PoolInfo ToPoolInfo(this PoolConfig pool, IMapper mapper, Persistence.Model.PoolStats stats)
        {
            var poolInfo = mapper.Map<PoolInfo>(pool);

            // enrich with basic information
            poolInfo.Coin.Algorithm = GetPoolAlgorithm(pool);

            poolInfo.PoolStats = mapper.Map<PoolStats>(stats);
            poolInfo.NetworkStats = mapper.Map<BlockchainStats>(stats);

            // pool wallet link
            CoinMetaData.AddressInfoLinks.TryGetValue(pool.Coin.Type, out var addressInfobaseUrl);
            if (!string.IsNullOrEmpty(addressInfobaseUrl))
                poolInfo.AddressInfoLink = string.Format(addressInfobaseUrl, poolInfo.Address);

            // pool fees
            poolInfo.PoolFeePercent = (float)pool.RewardRecipients.Sum(x => x.Percentage);

            // strip security critical stuff
            if (poolInfo.PaymentProcessing.Extra != null)
            {
                var extra = poolInfo.PaymentProcessing.Extra;

                extra.StripValue(nameof(EthereumPoolPaymentProcessingConfigExtra.CoinbasePassword));
            }

            return poolInfo;
        }

        private static string GetPoolAlgorithm(PoolConfig pool)
        {
            var result = pool.Coin.Algorithm;

            if (string.IsNullOrEmpty(result))
            {
                if (CoinMetaData.CoinAlgorithm.TryGetValue(pool.Coin.Type, out var getter))
                    result = getter(pool.Coin.Type);
            }

            // Capitalize
            if (!string.IsNullOrEmpty(result) && result.Length > 1)
                result = result.Substring(0, 1).ToUpper() + result.Substring(1);

            return result;
        }
    }
}