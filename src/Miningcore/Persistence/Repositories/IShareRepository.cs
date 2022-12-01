using System.Data;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;

namespace Miningcore.Persistence.Repositories;

public interface IShareRepository
{
    Task BatchInsertAsync(IDbConnection con, IDbTransaction tx, IEnumerable<Share> shares, CancellationToken ct);
    Task<Share[]> ReadSharesBeforeAsync(IDbConnection con, string poolId, DateTime before, bool inclusive, int pageSize, CancellationToken ct);
    Task<long> CountSharesBeforeAsync(IDbConnection con, IDbTransaction tx, string poolId, DateTime before, CancellationToken ct);
    Task DeleteSharesBeforeAsync(IDbConnection con, IDbTransaction tx, string poolId, DateTime before, CancellationToken ct);
    Task<long> CountSharesByMinerAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, CancellationToken ct);
    Task DeleteSharesByMinerAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, CancellationToken ct);
    Task<double?> GetAccumulatedShareDifficultyBetweenAsync(IDbConnection con, string poolId, DateTime start, DateTime end, CancellationToken ct);
    Task<double?> GetEffectiveAccumulatedShareDifficultyBetweenAsync(IDbConnection con, string poolId, DateTime start, DateTime end, CancellationToken ct);
    Task<double?> GetEffortBetweenCreatedAsync(IDbConnection con, string poolId, double shareConst, DateTime start, DateTime end);
    Task<MinerWorkerHashes[]> GetHashAccumulationBetweenAsync(IDbConnection con, string poolId, DateTime start, DateTime end, CancellationToken ct);
    Task<string[]> GetRecentyUsedIpAddressesAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, CancellationToken ct);

    Task<KeyValuePair<string, double>[]> GetAccumulatedUserAgentShareDifficultyBetweenAsync(IDbConnection con, string poolId,
        DateTime start, DateTime end, bool byVersione, CancellationToken ct);
}
