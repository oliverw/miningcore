using System.Linq;
using Autofac;
using AutoMapper;
using Miningcore.Api.Responses;
using Miningcore.Blockchain;
using Miningcore.Blockchain.Ethereum.Configuration;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Mining;

namespace Miningcore.Api.Extensions
{
    public static class MiningPoolExtensions
    {
        public static PoolInfo ToPoolInfo(this PoolConfig poolConfig,
                IMapper mapper, Persistence.Model.PoolStats stats, IMiningPool pool)
        {
            var poolInfo = mapper.Map<PoolInfo>(poolConfig);

            poolInfo.PoolStats = mapper.Map<PoolStats>(stats);
            poolInfo.NetworkStats = pool?.NetworkStats ?? mapper.Map<BlockchainStats>(stats);

            // pool wallet link
            var addressInfobaseUrl = poolConfig.Template.ExplorerAccountLink;
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
    }
}
