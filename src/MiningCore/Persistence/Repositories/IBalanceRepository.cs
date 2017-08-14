using System.Data;
using MiningCore.Configuration;
using MiningCore.Persistence.Model;

namespace MiningCore.Persistence.Repositories
{
    public interface IBalanceRepository
    {
        void AddAmount(IDbConnection con, IDbTransaction tx, string poolId, CoinType coin, string address,
            decimal amount);

        Balance[] GetPoolBalancesOverThreshold(IDbConnection con, string poolId, decimal minimum);
    }
}