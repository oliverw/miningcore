using System;
using System.Threading.Tasks;
using Miningcore.Persistence.Cosmos.Entities;

namespace Miningcore.Persistence.Repositories
{
    public interface IBalanceChangeRepository
    {
        Task AddNewBalanceChange(string poolId, string address, decimal amount, string usage);
        Task<BalanceChange> GetBalanceChangeByDate(string poolId, string address, DateTime created);
        Task UpdateBalanceChange(BalanceChange balanceChange);
    }
}
