using System;
using System.Data;
using AutoMapper;
using Dapper;
using MiningForce.Persistence.Model;
using MiningForce.Persistence.Repositories;

namespace MiningForce.Persistence.Postgres.Repositories
{
    public class BalanceRepository : IBalanceRepository
    {
		public BalanceRepository(IMapper mapper)
	    {
		    this.mapper = mapper;
	    }

	    private readonly IMapper mapper;

		public void InsertOrUpdate(IDbConnection con, IDbTransaction tx, Balance balance)
	    {
		    throw new NotImplementedException();
	    }
    }
}
