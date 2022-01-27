using Miningcore.Persistence.Model;

namespace Miningcore.Persistence.Repositories
{
    public interface IBalanceChangeRepository
    {
        Task AddNewBalanceChange(string poolId, string address, decimal amount, string usage);
        Task<BalanceChange> GetBalanceChangeByDate(string poolId, string address, DateTime created);
        Task UpdateBalanceChange(BalanceChange balanceChange);
        Task<uint> GetBalanceChangesCountAsync(string poolId, string address = null);
        Task<BalanceChange[]> PageBalanceChangesAsync(string poolId, string address, int page, int pageSize);
    }
}
