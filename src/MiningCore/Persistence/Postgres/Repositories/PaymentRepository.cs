using System.Data;
using AutoMapper;
using Dapper;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;

namespace MiningCore.Persistence.Postgres.Repositories
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly IMapper mapper;

        public PaymentRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        public void Insert(IDbConnection con, IDbTransaction tx, Payment payment)
        {
            var mapped = mapper.Map<Entities.Payment>(payment);

            var query = "INSERT INTO payments(poolid, coin, address, amount, transactionconfirmationdata, created) " +
                        "VALUES(@poolid, @coin, @address, @amount, @transactionconfirmationdata, @created)";

            con.Execute(query, mapped, tx);
        }
    }
}
