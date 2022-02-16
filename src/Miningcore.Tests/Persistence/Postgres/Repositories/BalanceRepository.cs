using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;

namespace Miningcore.Tests.Persistence.Postgres.Repositories
{
    public class BalanceRepository : IBalanceRepository
    {
        public Task<int> AddAmountAsync(IDbConnection con, IDbTransaction tx, string poolId, string address, decimal amount, string usage, params string[] tags)
        {
            throw new NotImplementedException();
        }

        public Task<decimal> GetBalanceAsync(IDbConnection con, string poolId, string address)
        {
            throw new NotImplementedException();
        }

        public Task<decimal> GetBalanceAsync(IDbConnection con, IDbTransaction tx, string poolId, string address)
        {
            throw new NotImplementedException();
        }

        public Task<Balance> GetBalanceDataAsync(IDbConnection con, string poolId, string address)
        {
            throw new NotImplementedException();
        }

        public Task<Balance> GetBalanceDataWithPaidDateAsync(IDbConnection con, string poolId, string address)
        {
            throw new NotImplementedException();
        }

        public Task<Balance[]> GetPoolBalancesOverThresholdAsync(IDbConnection con, string poolId, decimal minimum)
        {
            throw new NotImplementedException();
        }

        public Task<Balance[]> GetPoolBalancesOverThresholdAsync(IDbConnection con, string poolId, decimal minimum, int recordLimit)
        {
            return poolId switch
            {
                "eth1" => Task.FromResult(new Balance[]
                {
                    new()
                    {
                        Address = "0x471a8bf3fd0dfbe20658a97155388cec674190bf",
                        Amount = 0.01m,
                        Created = DateTime.UtcNow,
                        Updated = DateTime.UtcNow,
                        PaidDate = null,
                        PoolId = poolId
                    },
                    new()
                    {
                        Address = "0x4e65fda2159562a496f9f3522f89122a3088497a",
                        Amount = 0.05m,
                        Created = DateTime.UtcNow,
                        Updated = DateTime.UtcNow,
                        PaidDate = null,
                        PoolId = poolId
                    }
                }),
                _ => throw new NotImplementedException()
            };
        }

        public Task<List<BalanceSummary>> GetTotalBalanceSum(IDbConnection connection, string poolId, decimal minimum)
        {
            throw new NotImplementedException();
        }
    }
}
