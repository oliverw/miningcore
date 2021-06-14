/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Dapper;
using Miningcore.Extensions;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;
using Miningcore.Persistence.Repositories;
using NLog;

namespace Miningcore.Persistence.Postgres.Repositories
{
    public class PaymentRepository : IPaymentRepository
    {
        public PaymentRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        private readonly IMapper mapper;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public async Task InsertAsync(IDbConnection con, IDbTransaction tx, Payment payment)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.Payment>(payment);

            const string query = "INSERT INTO payments(poolid, coin, address, amount, transactionconfirmationdata, created) " +
                "VALUES(@poolid, @coin, @address, @amount, @transactionconfirmationdata, @created)";

            await con.ExecuteAsync(query, mapped, tx);
        }

        public async Task<Payment[]> PagePaymentsAsync(IDbConnection con, string poolId, string address, int page, int pageSize)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT * FROM payments WHERE poolid = @poolid ";

            if(!string.IsNullOrEmpty(address))
                query += " AND address = @address ";

            query += "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return (await con.QueryAsync<Entities.Payment>(query, new { poolId, address, offset = page * pageSize, pageSize }))
                .Select(mapper.Map<Payment>)
                .ToArray();
        }

        public async Task<BalanceChange[]> PageBalanceChangesAsync(IDbConnection con, string poolId, string address, int page, int pageSize)
        {
            logger.LogInvoke(new[] { poolId });

            const string query = "SELECT * FROM balance_changes WHERE poolid = @poolid " +
                "AND address = @address " +
                "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return (await con.QueryAsync<Entities.BalanceChange>(query, new { poolId, address, offset = page * pageSize, pageSize }))
                .Select(mapper.Map<BalanceChange>)
                .ToArray();
        }

        public async Task<AmountByDate[]> PageMinerPaymentsByDayAsync(IDbConnection con, string poolId, string address, int page, int pageSize)
        {
            logger.LogInvoke(new[] { poolId });

            const string query = "SELECT SUM(amount) AS amount, date_trunc('day', created) AS date FROM payments WHERE poolid = @poolid " +
                "AND address = @address " +
                "GROUP BY date " +
                "ORDER BY date DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return (await con.QueryAsync<AmountByDate>(query, new { poolId, address, offset = page * pageSize, pageSize }))
                .ToArray();
        }

        public Task<uint> GetPaymentsCountAsync(IDbConnection con, string poolId, string address = null)
        {
            logger.LogInvoke(new[] { poolId });

            string query = "SELECT COUNT(*) FROM payments WHERE poolid = @poolId";

            if(!string.IsNullOrEmpty(address))
                query += " AND address = @address ";


            return con.ExecuteScalarAsync<uint>(query, new { poolId, address });
        }

        public Task<uint> GetMinerPaymentsByDayCountAsync(IDbConnection con, string poolId, string address)
        {
            logger.LogInvoke(new[] { poolId });

            const string query = "SELECT COUNT(*) FROM (SELECT SUM(amount) AS amount, date_trunc('day', created) AS date FROM payments WHERE poolid = @poolid " +
                "AND address = @address " +
                "GROUP BY date " +
                "ORDER BY date DESC) s";

            return con.ExecuteScalarAsync<uint>(query, new { poolId, address });
        }

        public Task<uint> GetBalanceChangesCountAsync(IDbConnection con, string poolId, string address = null)
        {
            logger.LogInvoke(new[] { poolId });

            string query = "SELECT COUNT(*) FROM balance_changes WHERE poolid = @poolId";

            if(!string.IsNullOrEmpty(address))
                query += " AND address = @address ";


            return con.ExecuteScalarAsync<uint>(query, new { poolId, address });
        }
    }
}
