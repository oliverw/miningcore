using System.Data;
using MiningCore.Persistence.Model;

namespace MiningCore.Persistence.Repositories
{
    public interface IStatsRepository
    {
        void Insert(IDbConnection con, IDbTransaction tx, PoolStats share);
    }
}
