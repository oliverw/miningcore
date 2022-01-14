using System.Data;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;

namespace Miningcore.Persistence.Repositories;

public interface IShareRepository
{
    Task BatchInsertAsync(IDbConnection con, IDbTransaction tx, IEnumerable<Share> shares);
    Task<Share[]> ReadSharesBeforeCreatedAsync(IDbConnection con, string poolId, DateTime before, bool inclusive, int pageSize);
    Task<long> CountSharesBeforeCreatedAsync(IDbConnection con, IDbTransaction tx, string poolId, DateTime before);
    Task DeleteSharesBeforeCreatedAsync(IDbConnection con, IDbTransaction tx, string poolId, DateTime before);
    Task<long> CountSharesByMinerAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner);
    Task DeleteSharesByMinerAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner);
    Task<double?> GetAccumulatedShareDifficultyBetweenCreatedAsync(IDbConnection con, string poolId, DateTime start, DateTime end);
    Task<MinerWorkerHashes[]> GetHashAccumulationBetweenCreatedAsync(IDbConnection con, string poolId, DateTime start, DateTime end);
    Task<KeyValuePair<string, double>[]> GetAccumulatedUserAgentShareDifficultyBetweenCreatedAsync(IDbConnection con, string poolId, DateTime start, DateTime end, bool byVersion = false);
    Task<string[]> GetRecentyUsedIpAddressesAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner);
}
