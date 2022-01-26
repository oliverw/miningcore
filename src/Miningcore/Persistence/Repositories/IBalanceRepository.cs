using System.Data;
using Miningcore.Persistence.Model;

namespace Miningcore.Persistence.Repositories;

public interface IBalanceRepository
{
    Task<int> AddAmountAsync(IDbConnection con, IDbTransaction tx, string poolId, string address, decimal amount, string usage, params string[] tags);
    Task<decimal> GetBalanceAsync(IDbConnection con, string poolId, string address);
    Task<decimal> GetBalanceAsync(IDbConnection con, IDbTransaction tx, string poolId, string address);
    Task<Balance> GetBalanceDataAsync(IDbConnection con, string poolId, string address);
    Task<Balance> GetBalanceDataWithPaidDateAsync(IDbConnection con, string poolId, string address);
    Task<Balance[]> GetPoolBalancesOverThresholdAsync(IDbConnection con, string poolId, decimal minimum);
}
