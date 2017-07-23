using System.Data;
using AutoMapper;
using Dapper;
using MiningForce.Persistence.Model;
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

		public void Insert(IDbConnection con, IDbTransaction tx, Share share)
	    {
		    con.Execute("INSERT INTO shares(poolid, blockheight, difficulty, networkdifficulty, worker, ipaddress) " +
						"VALUES(@poolid, @blockheight, @difficulty, @networkdifficulty, @worker, @ipaddress)", share, tx);
	    }
    }
}
