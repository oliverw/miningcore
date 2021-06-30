using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;

namespace Miningcore.Persistence.Repositories
{
    public interface IPaymentRepository
    {
        Task InsertAsync(IDbConnection con, IDbTransaction tx, Payment payment);
        Task BatchInsertAsync(IDbConnection con, IDbTransaction tx, IEnumerable<Payment> shares);

        Task<Payment[]> PagePaymentsAsync(IDbConnection con, string poolId, string address, int page, int pageSize);
        Task<BalanceChange[]> PageBalanceChangesAsync(IDbConnection con, string poolId, string address, int page, int pageSize);
        Task<AmountByDate[]> PageMinerPaymentsByDayAsync(IDbConnection con, string poolId, string address, int page, int pageSize);
        Task<uint> GetPaymentsCountAsync(IDbConnection con, string poolId, string address = null);
        Task<uint> GetMinerPaymentsByDayCountAsync(IDbConnection con, string poolId, string address);
        Task<uint> GetBalanceChangesCountAsync(IDbConnection con, string poolId, string address = null);
    }
}
