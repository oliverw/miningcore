using System.Data;
using AutoMapper;
using Dapper;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;

namespace Miningcore.Persistence.Postgres.Repositories;

public class BalanceRepository : IBalanceRepository
{
    public BalanceRepository(IMapper mapper)
    {
        this.mapper = mapper;
    }

    private readonly IMapper mapper;

    public async Task<int> AddAmountAsync(IDbConnection con, IDbTransaction tx, string poolId, string address, decimal amount, string usage, params string[] tags)
    {
        var now = DateTime.UtcNow;

        // update balance
        var query = "SELECT * FROM balances WHERE poolid = @poolId AND address = @address";

        var balance = (await con.QueryAsync<Entities.Balance>(query, new { poolId, address }, tx))
            .FirstOrDefault();

        if(balance == null)
        {
            balance = new Entities.Balance
            {
                PoolId = poolId,
                Created = now,
                Address = address,
                Amount = amount,
                Updated = now
            };

            query = @"INSERT INTO balances(poolid, address, amount, created, updated)
                VALUES(@poolid, @address, @amount, @created, @updated)";

            return await con.ExecuteAsync(query, balance, tx);
        }

        else
        {
            query = @"UPDATE balances SET amount = amount + @amount, updated = now() at time zone 'utc'
                WHERE poolid = @poolId AND address = @address";

            return await con.ExecuteAsync(query, new
            {
                poolId,
                address,
                amount
            }, tx);
        }
    }

    public async Task<decimal> GetBalanceAsync(IDbConnection con, IDbTransaction tx, string poolId, string address)
    {
        const string query = @"SELECT amount FROM balances WHERE poolid = @poolId AND address = @address";

        return await con.QuerySingleOrDefaultAsync<decimal>(query, new { poolId, address }, tx);
    }

    public async Task<decimal> GetBalanceAsync(IDbConnection con, string poolId, string address)
    {
        const string query = @"SELECT amount FROM balances WHERE poolid = @poolId AND address = @address";

        return await con.QuerySingleOrDefaultAsync<decimal>(query, new { poolId, address });
    }

    public async Task<Balance[]> GetPoolBalancesOverThresholdAsync(IDbConnection con, string poolId, decimal minimum)
    {
        const string query = @"SELECT b.*
            FROM balances b
            LEFT JOIN miner_settings ms ON ms.poolid = b.poolid AND ms.address = b.address
            WHERE b.poolid = @poolId AND b.amount >= COALESCE(ms.paymentthreshold, @minimum)";

        return (await con.QueryAsync<Entities.Balance>(query, new { poolId, minimum }))
            .Select(mapper.Map<Balance>)
            .ToArray();
    }

    public Task<int> GetBalanceChangeCountByTagAsync(IDbConnection con, IDbTransaction tx, string poolId, string tag)
    {
        const string query = @"SELECT COUNT(*) FROM balance_changes WHERE poolid = @poolid AND @tag <@ tags";

        return con.ExecuteScalarAsync<int>(query, new { poolId, tag = new[] { tag } }, tx);
    }

    public async Task<BalanceChange[]> GetBalanceChangesByTagAsync(IDbConnection con, IDbTransaction tx, string poolId, string tag)
    {
        const string query = @"SELECT * FROM balance_changes WHERE poolid = @poolid
            AND @tag <@ tags
            ORDER BY created DESC";

        return (await con.QueryAsync<Entities.BalanceChange>(query, new { poolId, tag = new[] { tag } }, tx))
            .Select(mapper.Map<BalanceChange>)
            .ToArray();
    }

    public async Task<Balance> GetBalanceDataAsync(IDbConnection con, string poolId, string address)
    {
        const string query = "SELECT * FROM balances WHERE poolid = @poolId AND address = @address";

        return (await con.QueryAsync<Entities.Balance>(query, new { poolId, address }))
            .Select(mapper.Map<Balance>)
            .FirstOrDefault();
    }

    public async Task<Balance> GetBalanceDataWithPaidDateAsync(IDbConnection con, string poolId, string address)
    {
        const string query = "SELECT b.poolid, b.address, b.amount, b.created, b.updated, p.created AS paiddate FROM balances AS b " +
                          "LEFT JOIN payments AS p ON  p.address = b.address AND p.poolid = b.poolid " +
                          "WHERE b.poolid = @poolId AND b.address = @address ORDER BY p.created DESC LIMIT 1";

        return (await con.QueryAsync<Entities.Balance>(query, new { poolId, address }))
            .Select(mapper.Map<Balance>)
            .FirstOrDefault();
    }

}
