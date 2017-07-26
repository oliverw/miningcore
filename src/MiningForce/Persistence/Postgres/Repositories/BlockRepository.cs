using System;
using System.Data;
using System.Linq;
using AutoMapper;
using Dapper;
using MiningForce.Configuration;
using MiningForce.Persistence.Repositories;
using NLog;

namespace MiningForce.Persistence.Postgres.Repositories
{
    public class BlockRepository : IBlockRepository
    {
	    public BlockRepository(IMapper mapper)
	    {
		    this.mapper = mapper;
	    }

	    private readonly IMapper mapper;

		public void Insert(IDbConnection con, IDbTransaction tx, Model.Block block)
		{
			var mapped = mapper.Map<Entities.Block>(block);

			var query = "INSERT INTO blocks(poolid, blockheight, status, transactionconfirmationdata, reward, created) " +
						"VALUES(@poolid, @blockheight, @status, @transactionconfirmationdata, @reward, @created)";

			con.Execute(query, mapped, tx);
	    }

	    public void DeleteBlock(IDbConnection con, IDbTransaction tx, Model.Block block)
	    {
		    var query = "DELETE FROM blocks WHERE id = @id";
		    con.Execute(query, block, tx);
	    }

	    public void UpdateBlock(IDbConnection con, IDbTransaction tx, Model.Block block)
	    {
		    var mapped = mapper.Map<Entities.Block>(block);

		    var query = "UPDATE blocks SET status = @status, reward = @reward WHERE id = @id";
		    con.Execute(query, mapped, tx);
	    }

		public Model.Block[] GetPendingBlocksForPool(IDbConnection con, string poolid)
	    {
		    var query = "SELECT * FROM blocks WHERE poolid = @poolid AND status = @status";

		    return con.Query<Entities.Block>(query, new { status = Model.BlockStatus.Pending.ToString().ToLower(), poolid })
			    .Select(mapper.Map<Model.Block>)
			    .ToArray();
	    }
	}
}
