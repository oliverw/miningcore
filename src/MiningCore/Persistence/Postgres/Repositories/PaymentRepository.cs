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
using AutoMapper;
using Dapper;
using MiningCore.Extensions;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using MiningCore.Util;
using NLog;

namespace MiningCore.Persistence.Postgres.Repositories
{
    public class PaymentRepository : IPaymentRepository
    {
        public PaymentRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        private readonly IMapper mapper;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public void Insert(IDbConnection con, IDbTransaction tx, Payment payment)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.Payment>(payment);

            var query = "INSERT INTO payments(poolid, coin, address, amount, transactionconfirmationdata, created) " +
                "VALUES(@poolid, @coin, @address, @amount, @transactionconfirmationdata, @created)";

            con.Execute(query, mapped, tx);
        }

        public Payment[] PagePayments(IDbConnection con, string poolId, string address, int page, int pageSize)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT * FROM payments WHERE poolid = @poolid ";

            if (!string.IsNullOrEmpty(address))
                query += " AND address = @address ";

            query += "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return con.Query<Entities.Payment>(query, new { poolId, address, offset = page * pageSize, pageSize })
                .Select(mapper.Map<Payment>)
                .ToArray();
        }

        public BalanceChange[] PageBalanceChanges(IDbConnection con, string poolId, string address, int page, int pageSize)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT * FROM balance_changes WHERE poolid = @poolid " +
                        "AND address = @address " +
                        "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return con.Query<Entities.BalanceChange>(query, new { poolId, address, offset = page * pageSize, pageSize })
                .Select(mapper.Map<BalanceChange>)
                .ToArray();
        }
    }
}
