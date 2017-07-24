using System;
using System.Data;
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

	    public Model.Share[] PageSharesBefore(DateTime before, int page, int pageSize)
	    {
		    throw new NotImplementedException();
	    }
    }
}
