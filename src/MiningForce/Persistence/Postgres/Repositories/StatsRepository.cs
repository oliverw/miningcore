using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Dapper;
using MiningForce.Blockchain;
using MiningForce.Mining;
using MiningForce.Persistence.Repositories;

namespace MiningForce.Persistence.Postgres.Repositories
{
    public class StatsRepository : IStatsRepository
    {
	    public StatsRepository(IMapper mapper)
	    {
		    this.mapper = mapper;
	    }

	    private readonly IMapper mapper;

	    #region Implementation of IStatsRepository

	    public void UpdatePoolStats(IDbConnection con, IDbTransaction tx, string poolId, PoolStats poolStats,
		    BlockchainStats blockchainStats)
	    {
		    var mapped = mapper.Map(poolStats, // merge both stats into one entity
				mapper.Map<Entities.PoolAndBlockchainStats>(blockchainStats));

		    mapped.PoolId = poolId;

			var query = "INSERT INTO poolstats(poolid, lastpoolblocktime, connectedminers, poolhashrate, " +
		                "poolfeepercent, donationspercent, sharespersecond, " +
		                "validsharesperminute, invalidsharesperminute, networktype, networkhashrate, " +
		                "lastnetworkblocktime, networkdifficulty, blockheight, connectedpeers, rewardtype, " +
		                "created, updated) " +
		                "" +
						"VALUES(@poolid, @lastpoolblocktime, @connectedminers, @poolhashrate, " +
		                "@poolfeepercent, @donationspercent, @sharespersecond, " +
		                "@validsharesperminute, @invalidsharesperminute, @networktype, @networkhashrate, " +
		                "@lastnetworkblocktime, @networkdifficulty, @blockheight, @connectedpeers, @rewardtype, " +
						"now() at time zone 'utc', now() at time zone 'utc') " +
		                "ON CONFLICT (poolid) DO " +
		                "" +
		                "UPDATE SET " +
						"poolid = @poolid, lastpoolblocktime = @lastpoolblocktime, connectedminers = @connectedminers, " +
		                "poolhashrate = @poolhashrate, poolfeepercent = @poolfeepercent, donationspercent = @donationspercent, " +
		                "sharespersecond = @sharespersecond, " +
		                "validsharesperminute = @validsharesperminute, invalidsharesperminute = @invalidsharesperminute, " +
		                "networktype = @networktype, networkhashrate = @networkhashrate, " +
		                "lastnetworkblocktime = @lastnetworkblocktime, networkdifficulty = @networkdifficulty, " +
		                "blockheight = @blockheight, connectedpeers = @connectedpeers, rewardtype = @rewardtype, " +
						"updated = now() at time zone 'utc' ";

		    con.Execute(query, mapped, tx);
	    }

		public async Task<(PoolStats PoolStats, BlockchainStats NetworkStats)> GetPoolStatsAsync(IDbConnection con, string poolId)
	    {
		    var query = "SELECT * FROM poolstats WHERE poolid = @poolId";

		    var entity = await con.QuerySingleOrDefaultAsync<Entities.PoolAndBlockchainStats>(query, new { poolId });
		    if (entity == null)
			    return ((PoolStats) null, (BlockchainStats) null);

		    var poolStats = mapper.Map<PoolStats>(entity);
		    var blockchainStats = mapper.Map<BlockchainStats>(entity);

		    return (poolStats, blockchainStats);
	    }

		#endregion
	}
}
