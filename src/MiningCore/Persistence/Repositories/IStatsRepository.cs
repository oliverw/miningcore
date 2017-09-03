using System;
using System.Data;
using Dapper;
using MiningCore.Persistence.Model;

namespace MiningCore.Persistence.Repositories
{
    public interface IStatsRepository
    {
        void Insert(IDbConnection con, IDbTransaction tx, PoolStats share);

        PoolStats[] PageStatsBetween(IDbConnection con, string poolId, DateTime start, DateTime end, int page,
            int pageSize);
    }
}
