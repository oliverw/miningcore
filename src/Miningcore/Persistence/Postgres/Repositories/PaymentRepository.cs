using System.Data;
using AutoMapper;
using Dapper;
using JetBrains.Annotations;
using Miningcore.Extensions;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;
using Miningcore.Persistence.Repositories;
using NLog;
using Npgsql;
using NpgsqlTypes;

namespace Miningcore.Persistence.Postgres.Repositories;

[UsedImplicitly]
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

        const string query = @"INSERT INTO payments(poolid, coin, address, amount, transactionconfirmationdata, created)
            VALUES(@poolid, @coin, @address, @amount, @transactionconfirmationdata, @created)";

        await con.ExecuteAsync(query, mapped, tx);
    }

    public async Task BatchInsertAsync(IDbConnection con, IDbTransaction tx, IEnumerable<Payment> payments)
    {
        logger.LogInvoke();

        // NOTE: Even though the tx parameter is completely ignored here,
        // the COPY command still honors a current ambient transaction

        var pgCon = (NpgsqlConnection) con;

        const string query = @"COPY payments (poolid, coin, address, amount, transactionconfirmationdata, created) FROM STDIN (FORMAT BINARY)";

        await using(var writer = await pgCon.BeginBinaryImportAsync(query))
        {
            foreach(var payment in payments)
            {
                await writer.StartRowAsync();

                await writer.WriteAsync(payment.PoolId);
                await writer.WriteAsync(payment.Coin);
                await writer.WriteAsync(payment.Address);
                await writer.WriteAsync(payment.Amount, NpgsqlDbType.Numeric);
                await writer.WriteAsync(payment.TransactionConfirmationData);
                await writer.WriteAsync(payment.Created, NpgsqlDbType.Timestamp);
            }

            await writer.CompleteAsync();
        }
    }

    public async Task<Payment[]> PagePaymentsAsync(IDbConnection con, string poolId, string address, int page, int pageSize)
    {
        logger.LogInvoke(new object[] { poolId });

        var query = @"SELECT * FROM payments WHERE poolid = @poolid ";

        if(!string.IsNullOrEmpty(address))
            query += " AND address = @address ";

        query += "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

        return (await con.QueryAsync<Entities.Payment>(query, new { poolId, address, offset = page * pageSize, pageSize }))
            .Select(mapper.Map<Payment>)
            .ToArray();
    }

    public async Task<AmountByDate[]> PageMinerPaymentsByDayAsync(IDbConnection con, string poolId, string address, int page, int pageSize)
    {
        logger.LogInvoke(new object[] { poolId });

        const string query = @"SELECT SUM(amount) AS amount, date_trunc('day', created) AS date FROM payments WHERE poolid = @poolid
            AND address = @address
            GROUP BY date
            ORDER BY date DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

        return (await con.QueryAsync<AmountByDate>(query, new { poolId, address, offset = page * pageSize, pageSize }))
            .ToArray();
    }

    public Task<uint> GetPaymentsCountAsync(IDbConnection con, string poolId, string address = null)
    {
        logger.LogInvoke(new object[] { poolId });

        string query = @"SELECT COUNT(*) FROM payments WHERE poolid = @poolId";

        if(!string.IsNullOrEmpty(address))
            query += " AND address = @address ";


        return con.ExecuteScalarAsync<uint>(query, new { poolId, address });
    }

    public Task<uint> GetMinerPaymentsByDayCountAsync(IDbConnection con, string poolId, string address)
    {
        logger.LogInvoke(new object[] { poolId });

        const string query =
            @"SELECT COUNT(*) FROM (SELECT SUM(amount) AS amount, date_trunc('day', created) AS date FROM payments WHERE poolid = @poolid
            AND address = @address
            GROUP BY date
            ORDER BY date DESC) s";

        return con.ExecuteScalarAsync<uint>(query, new { poolId, address });
    }

    public async Task<PoolState> GetPoolState(IDbConnection con, string poolId)
    {
        logger.LogInvoke();

        const string query = "SELECT poolid, hashvalue, lastpayout FROM poolstate WHERE poolid = @poolId";

        return await con.QuerySingleOrDefaultAsync<PoolState>(query, new { poolId });
    }

    public async Task SetPoolState(IDbConnection con, PoolState state)
    {
        logger.LogInvoke();

        var mapped = mapper.Map<Entities.PoolState>(state);

        const string query = @"INSERT INTO poolstate as ps(poolid, hashvalue, lastpayout) VALUES (@poolId, @hashValue, @lastpayout)
            ON CONFLICT (poolid)
            DO UPDATE SET hashvalue = (CASE WHEN EXCLUDED.hashvalue ISNULL OR EXCLUDED.hashvalue=0 THEN ps.hashvalue ELSE EXCLUDED.hashvalue END), lastpayout = COALESCE(EXCLUDED.lastpayout, ps.lastpayout)";

        await con.ExecuteAsync(query, mapped);
    }
}
