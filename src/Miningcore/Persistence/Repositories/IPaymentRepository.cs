using System.Data;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;

namespace Miningcore.Persistence.Repositories;

public interface IPaymentRepository
{
    Task InsertAsync(IDbConnection con, IDbTransaction tx, Payment payment);
    Task BatchInsertAsync(IDbConnection con, IDbTransaction tx, IEnumerable<Payment> shares);

    Task<Payment[]> PagePaymentsAsync(IDbConnection con, string poolId, string address, int page, int pageSize, CancellationToken ct);
    Task<BalanceChange[]> PageBalanceChangesAsync(IDbConnection con, string poolId, string address, int page, int pageSize, CancellationToken ct);
    Task<AmountByDate[]> PageMinerPaymentsByDayAsync(IDbConnection con, string poolId, string address, int page, int pageSize, CancellationToken ct);
    Task<uint> GetPaymentsCountAsync(IDbConnection con, string poolId, string address, CancellationToken ct);
    Task<uint> GetMinerPaymentsByDayCountAsync(IDbConnection con, string poolId, string address);
    Task<PoolState> GetPoolState(IDbConnection con, string poolId);
    Task SetPoolState(IDbConnection con, PoolState state);
}
