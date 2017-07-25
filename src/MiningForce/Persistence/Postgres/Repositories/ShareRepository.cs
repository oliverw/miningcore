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
		    con.Execute("INSERT INTO shares(poolid, blockheight, difficulty, networkdifficulty, worker, ipaddress, created) " +
						"VALUES(@poolid, @blockheight, @difficulty, @networkdifficulty, @worker, @ipaddress, @created)", share, tx);
	    }

	    public Model.Share[] PageSharesBefore(IDbConnection con, DateTime before, int page, int pageSize)
	    {
		    return con.Query<Entities.Share>("SELECT * FROM shares WHERE created < @before " +
		                                     "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY",
				    new { before, offset = page * pageSize, pageSize })
			    .Select(mapper.Map<Model.Share>)
			    .ToArray();
	    }
	}
}
