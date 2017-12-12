using System.Linq;
using AutoMapper;
using MiningCore.Api.Responses;
using MiningCore.Blockchain;
using MiningCore.Mining;

namespace MiningCore.Api.Extensions
{
    public static class MiningPoolExtensions
    {
        public static PoolInfo ToPoolInfo(this IMiningPool pool, IMapper mapper)
        {
            var poolInfo = mapper.Map<PoolInfo>(pool.Config);

            poolInfo.PoolStats = pool.PoolStats;
            poolInfo.NetworkStats = pool.NetworkStats;

            // pool wallet link
            CoinMetaData.AddressInfoLinks.TryGetValue(pool.Config.Coin.Type, out var addressInfobaseUrl);
            if (!string.IsNullOrEmpty(addressInfobaseUrl))
                poolInfo.AddressInfoLink = string.Format(addressInfobaseUrl, poolInfo.Address);

            // pool fees
            poolInfo.PoolFeePercent = (float)pool.Config.RewardRecipients
                .Sum(x => x.Percentage);

            return poolInfo;
        }
    }
}