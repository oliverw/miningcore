using System;
using System.Data;
using System.Linq;
using AutoMapper;
using Dapper;
using MiningCore.Configuration;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;

namespace MiningCore.Persistence.Postgres.Repositories
{
    public class BalanceRepository : IBalanceRepository
    {
		public BalanceRepository(IMapper mapper)
	    {
		    this.mapper = mapper;
	    }

	    private readonly IMapper mapper;

	    public void AddAmount(IDbConnection con, IDbTransaction tx, string poolId, CoinType coin, string address, decimal amount)
	    {
		    var query = "SELECT * FROM balances WHERE poolid = @poolId AND coin = @coin AND address = @address";

			var balance = con.Query<Entities.Balance>(query, new { poolId, coin = coin.ToString(), address }, tx)
				.FirstOrDefault();

		    var now = DateTime.UtcNow;

			if (balance == null)
		    {
			    balance = new Entities.Balance
			    {
					PoolId = poolId,
				    Coin = coin.ToString(),
				    Created = now,
				    Address = address,
					Amount = amount,
					Updated = now,
				};

			    query = "INSERT INTO balances(poolid, coin, address, amount, created, updated) " +
			            "VALUES(@poolid, @coin, @address, @amount, @created, @updated)";

				con.Execute(query, balance, tx);
			}

			else
			{
				balance.Updated = now;
				balance.Amount += amount;

				query = "UPDATE balances SET amount = @amount, updated = @updated " +
				        "WHERE poolid = @poolId AND coin = @coin AND address = @address";
				con.Execute(query, balance, tx);
			}
	    }

	    public Balance[] GetPoolBalancesOverThreshold(IDbConnection con, string poolId, decimal minimum)
	    {
		    var query = "SELECT * FROM balances WHERE poolid = @poolId AND amount >= @minimum";

		    return con.Query<Entities.Balance>(query, new { poolId, minimum })
			    .Select(mapper.Map<Balance>)
			    .ToArray();
	    }
	}
}
