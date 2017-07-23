using System.Data;
using Dapper;
using MiningForce.Persistence.Model;
using MiningForce.Persistence.Repositories;

namespace MiningForce.Persistence.Postgres.Repositories
{
    public class ShareRepository : IShareRepository
    {
	    public void Insert(IDbConnection con, IDbTransaction tx, Share share)
	    {
		    con.Execute("INSERT INTO shares(coin, blockheight, difficulty, networkdifficulty, worker, ipaddress) " +
						"VALUES(@coin, @blockheight, @difficulty, @networkdifficulty, @worker, @ipaddress)", share, tx);
	    }
    }
}
