using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;
using Miningcore.Persistence.Repositories;

namespace Miningcore.Tests.Persistence.Postgres.Repositories
{
    public class PaymentRepository : IPaymentRepository
    {
        public PaymentRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        private readonly IMapper mapper;

        public Task InsertAsync(IDbConnection con, IDbTransaction tx, Payment payment)
        {
            throw new NotImplementedException();
        }
        public Task BatchInsertAsync(IDbConnection con, IDbTransaction tx, IEnumerable<Payment> shares)
        {
            throw new NotImplementedException();
        }

        public Task<Payment[]> PagePaymentsAsync(IDbConnection con, string poolId, string address, int page, int pageSize, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task<BalanceChange[]> PageBalanceChangesAsync(IDbConnection con, string poolId, string address, int page, int pageSize, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task<AmountByDate[]> PageMinerPaymentsByDayAsync(IDbConnection con, string poolId, string address, int page, int pageSize, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task<uint> GetPaymentsCountAsync(IDbConnection con, string poolId, string address, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task<uint> GetMinerPaymentsByDayCountAsync(IDbConnection con, string poolId, string address)
        {
            throw new NotImplementedException();
        }
        public Task<PoolState> GetPoolState(IDbConnection con, string poolId)
        {
            throw new NotImplementedException();
        }
        public Task SetPoolState(IDbConnection con, PoolState state)
        {
            throw new NotImplementedException();
        }
    }
}
