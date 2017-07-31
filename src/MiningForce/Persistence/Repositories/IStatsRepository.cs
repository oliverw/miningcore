using System;
using System.Data;
using System.Threading.Tasks;
using MiningForce.Blockchain;
using MiningForce.Mining;

namespace MiningForce.Persistence.Repositories
{
    public interface IStatsRepository
	{
		void UpdatePoolStats(IDbConnection con, IDbTransaction tx, string poolId, PoolStats poolStats, BlockchainStats blockchainStats);
		Task<(PoolStats PoolStats, BlockchainStats NetworkStats)> GetPoolStatsAsync(IDbConnection con, string poolId);
	}
}
