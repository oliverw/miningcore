using System;
using System.Data;
using System.Linq;
using AutoMapper;
using Dapper;
using MiningForce.Persistence.Repositories;

namespace MiningForce.Persistence.Postgres.Repositories
{
    public class ShareRepository : IShareRepository
    {
	    public ShareRepository(IMapper mapper)
	    {
		    this.mapper = mapper;
	    }

	    private readonly IMapper mapper;

		public void Insert(IDbConnection con, IDbTransaction tx, Model.Share share)
		{
			var mapped = mapper.Map<Entities.Share>(share);

			var query = "INSERT INTO shares(poolid, blockheight, difficulty, stratumdifficulty, networkdifficulty, worker, ipaddress, created) " +
						"VALUES(@poolid, @blockheight, @difficulty, @stratumdifficulty, @networkdifficulty, @worker, @ipaddress, @created)";

			con.Execute(query, mapped, tx);
	    }

	    public Model.Share[] PageSharesBefore(IDbConnection con, string poolId, DateTime before, int page, int pageSize)
	    {
		    var query = "SELECT * FROM shares WHERE poolid = @poolId AND created < @before " +
		                "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

			return con.Query<Entities.Share>(query, new { poolId, before, offset = page * pageSize, pageSize })
			    .Select(mapper.Map<Model.Share>)
			    .ToArray();
	    }

	    public long CountSharesBefore(IDbConnection con, IDbTransaction tx, string poolId, DateTime before)
	    {
		    var query = "SELECT count(*) FROM shares WHERE poolid = @poolId AND created < @before";

		    return con.QuerySingle<long>(query, new { poolId, before }, tx);
	    }

		public void DeleteSharesBefore(IDbConnection con, IDbTransaction tx, string poolId, DateTime before)
	    {
		    var query = "DELETE FROM shares WHERE poolid = @poolId AND created < @before";

			con.Execute(query, new { poolId, before }, tx);
	    }
	}
}
