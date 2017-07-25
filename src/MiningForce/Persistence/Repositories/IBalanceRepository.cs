using System.Data;
using MiningForce.Configuration;
using MiningForce.Persistence.Model;

namespace MiningForce.Persistence.Repositories
{
    public interface IBalanceRepository
	{
		void AddAmount(IDbConnection con, IDbTransaction tx, string poolId, CoinType coin, string address, double amount);
		Balance[] GetPoolBalancesOverThreshold(IDbConnection con, string poolId, double minimum);
	}
}
