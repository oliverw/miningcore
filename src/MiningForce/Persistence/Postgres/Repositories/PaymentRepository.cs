using System.Data;
using AutoMapper;
using Dapper;
using MiningForce.Persistence.Model;
using MiningForce.Persistence.Repositories;

namespace MiningForce.Persistence.Postgres.Repositories
{
    public class PaymentRepository : IPaymentRepository
    {
	    public PaymentRepository(IMapper mapper)
	    {
		    this.mapper = mapper;
	    }

	    private readonly IMapper mapper;

		public void Insert(IDbConnection con, IDbTransaction tx, Payment payment)
	    {
		    var mapped = mapper.Map<Entities.Payment>(payment);

		    var query = "INSERT INTO payments(poolid, coin, address, amount, transactionconfirmationdata, created) " +
						"VALUES(@poolid, @coin, @address, @amount, @transactionconfirmationdata, @created)";

		    con.Execute(query, mapped, tx);
	    }
	}
}
