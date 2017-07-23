using System;
using System.Data;
using Dapper;
using MiningForce.Persistence.Model;
using MiningForce.Persistence.Repositories;

namespace MiningForce.Persistence.Postgres.Repositories
{
    public class PaymentRepository : IPaymentRepository
    {
	    public void Insert(IDbConnection con, IDbTransaction tx, Payment payment)
	    {
		    throw new NotImplementedException();
	    }
    }
}
