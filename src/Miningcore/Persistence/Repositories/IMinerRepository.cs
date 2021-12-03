using System.Data;
using Miningcore.Persistence.Model;

namespace Miningcore.Persistence.Repositories;

public interface IMinerRepository
{
    Task<MinerSettings> GetSettings(IDbConnection con, IDbTransaction tx, string poolId, string address);
    Task UpdateSettings(IDbConnection con, IDbTransaction tx, MinerSettings settings);
}
