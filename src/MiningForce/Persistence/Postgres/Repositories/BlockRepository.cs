using System;
using System.Data;
using Dapper;
using MiningForce.Blockchain;
using MiningForce.Persistence.Repositories;
using NLog;

namespace MiningForce.Persistence.Postgres.Repositories
{
    public class BlockRepository : IBlockRepository
    {
		public void Insert(IDbConnection con, IDbTransaction tx, Model.Block block)
	    {
			con.Execute("INSERT INTO blocks(coin, blockheight, status, transactionconfirmationdata) " +
						"VALUES(@coin, @blockheight, @status, @transactionconfirmationdata)", block, tx);
	    }
	}
}
