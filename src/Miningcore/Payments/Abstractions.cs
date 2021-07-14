using System.Data;
using System.Threading.Tasks;
using Miningcore.Configuration;
using Miningcore.Mining;
using Miningcore.Persistence.Model;

namespace Miningcore.Payments
{
    public interface IPayoutHandler
    {
        Task ConfigureAsync(ClusterConfig clusterConfig, PoolConfig poolConfig);

        Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks);
        Task CalculateBlockEffortAsync(IMiningPool pool, Block block, double accumulatedBlockShareDiff);
        Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, IMiningPool pool, Block block);
        Task PayoutAsync(IMiningPool pool, Balance[] balances);

        string FormatAmount(decimal amount);
        double AdjustShareDifficulty(double difficulty);
    }

    public interface IPayoutScheme
    {
        Task UpdateBalancesAsync(IDbConnection con, IDbTransaction tx, IMiningPool pool, IPayoutHandler payoutHandler, Block block, decimal blockReward);
    }
}
