using System.Data;
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
    }
}
