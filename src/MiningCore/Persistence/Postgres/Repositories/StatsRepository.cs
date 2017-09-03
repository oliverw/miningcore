using System;
using System.Data;
using System.Linq;
using AutoMapper;
using Dapper;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;

namespace MiningCore.Persistence.Postgres.Repositories
{
    public class StatsRepository : IStatsRepository
    {
        public StatsRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        private readonly IMapper mapper;

        public void Insert(IDbConnection con, IDbTransaction tx, PoolStats stats)
        {
            var mapped = mapper.Map<Entities.PoolStats>(stats);

            var query = "INSERT INTO poolstats(poolid, connectedminers, poolhashrate, sharespersecond, " +
                        "validsharesperminute, invalidsharesperminute, created) " +
                        "VALUES(@poolid, @connectedminers, @poolhashrate, @sharespersecond, @validsharesperminute, " +
                        "@invalidsharesperminute, @created)";

            con.Execute(query, mapped, tx);
        }

        public PoolStats[] PageStatsBetween(IDbConnection con, string poolId, DateTime start, DateTime end, int page, int pageSize)
        {
            var query = "SELECT * FROM poolstats WHERE poolid = @poolId AND created >= @start AND created <= @end " +
                        "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return con.Query<Entities.PoolStats>(query, new { poolId, start, end, offset = page * pageSize, pageSize })
                .Select(mapper.Map<PoolStats>)
                .ToArray();
        }
    }
}
