using System;
using System.Data;
using Dapper;
using MiningForce.Persistence.Model;
using MiningForce.Persistence.Repositories;

namespace MiningForce.Persistence.Postgres.Repositories
{
    public class BalanceRepository : IBalanceRepository
    {
	    public void InsertOrUpdate(IDbConnection con, IDbTransaction tx, Balance balance)
	    {
		    throw new NotImplementedException();
	    }
    }
}
