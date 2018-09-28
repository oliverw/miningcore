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
        public static PoolInfo ToPoolInfo(this PoolConfig poolConfig, IMapper mapper, Persistence.Model.PoolStats stats, IMiningPool pool)
        {
            var poolInfo = mapper.Map<PoolInfo>(poolConfig);

            // enrich with basic information
            poolInfo.Coin.Algorithm = GetPoolAlgorithm(poolConfig);

            poolInfo.PoolStats = mapper.Map<PoolStats>(stats);
            poolInfo.NetworkStats = pool?.NetworkStats ?? mapper.Map<BlockchainStats>(stats);

            // pool wallet link
            CoinMetaData.AddressInfoLinks.TryGetValue(poolConfig.Coin.Type, out var addressInfobaseUrl);
            if (!string.IsNullOrEmpty(addressInfobaseUrl))
                poolInfo.AddressInfoLink = string.Format(addressInfobaseUrl, poolInfo.Address);

            // pool fees
            poolInfo.PoolFeePercent = (float) poolConfig.RewardRecipients.Sum(x => x.Percentage);

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
            string result = null;

            if (CoinMetaData.CoinAlgorithm.TryGetValue(pool.Coin.Type, out var getter))
                result = getter(pool.Coin.Type, pool.Coin.Algorithm);

            // Capitalize
            if (!string.IsNullOrEmpty(result) && result.Length > 1)
                result = result.Substring(0, 1).ToUpper() + result.Substring(1);

            return result;
        }
    }
}
