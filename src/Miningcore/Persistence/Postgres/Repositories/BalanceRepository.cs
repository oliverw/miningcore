using System.Data;
using AutoMapper;
using Dapper;
using JetBrains.Annotations;
using Miningcore.Extensions;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using NLog;

namespace Miningcore.Persistence.Postgres.Repositories;

[UsedImplicitly]
public class BalanceRepository : IBalanceRepository
{
    public BalanceRepository(IMapper mapper)
    {
        this.mapper = mapper;
    }

    private readonly IMapper mapper;
    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

    public async Task<int> AddAmountAsync(IDbConnection con, IDbTransaction tx, string poolId, string address, decimal amount, string usage, params string[] tags)
    {
        logger.LogInvoke();

        const string query = @"INSERT INTO balances(poolid, address, amount, created, updated)
                VALUES(@poolId, @address, @amount, @created, @updated)
                ON CONFLICT (poolid, address)
                DO UPDATE SET amount = balances.amount + EXCLUDED.amount, updated = EXCLUDED.updated";

        var now = DateTime.UtcNow;

        var balance = new Entities.Balance
        {
            PoolId = poolId,
            Created = now,
            Address = address,
            Amount = amount,
            Updated = now
        };

        return await con.ExecuteAsync(query, balance, tx);
    }

    public async Task<decimal> GetBalanceAsync(IDbConnection con, IDbTransaction tx, string poolId, string address)
    {
        logger.LogInvoke();

        const string query = @"SELECT amount FROM balances WHERE poolid = @poolId AND address = @address";

        return await con.QuerySingleOrDefaultAsync<decimal>(query, new { poolId, address }, tx);
    }

    public async Task<decimal> GetBalanceAsync(IDbConnection con, string poolId, string address)
    {
        logger.LogInvoke();

        const string query = @"SELECT amount FROM balances WHERE poolid = @poolId AND address = @address";

        return await con.QuerySingleOrDefaultAsync<decimal>(query, new { poolId, address });
    }

    public async Task<Balance> GetBalanceDataAsync(IDbConnection con, string poolId, string address)
    {
        logger.LogInvoke();

        const string query = "SELECT * FROM balances WHERE poolid = @poolId AND address = @address";

        return (await con.QueryAsync<Entities.Balance>(query, new { poolId, address }))
            .Select(mapper.Map<Balance>)
            .FirstOrDefault();
    }

    public async Task<Balance> GetBalanceDataWithPaidDateAsync(IDbConnection con, string poolId, string address)
    {
        logger.LogInvoke();

        const string query = "SELECT b.poolid, b.address, b.amount, b.created, b.updated, p.created AS paiddate FROM balances AS b " +
                                "LEFT JOIN payments AS p ON  p.address = b.address AND p.poolid = b.poolid " +
                                "WHERE b.poolid = @poolId AND b.address = @address ORDER BY p.created DESC LIMIT 1";

        return (await con.QueryAsync<Entities.Balance>(query, new { poolId, address }))
            .Select(mapper.Map<Balance>)
            .FirstOrDefault();
    }

    public async Task<Balance[]> GetPoolBalancesOverThresholdAsync(IDbConnection con, string poolId, decimal minimum)
    {
        logger.LogInvoke();

        const string query = @"SELECT b.*
            FROM balances b
            LEFT JOIN miner_settings ms ON ms.poolid = b.poolid AND ms.address = b.address
            WHERE b.poolid = @poolId AND b.amount >= COALESCE(ms.paymentthreshold, @minimum)
            GROUP BY b.poolid, b.address, b.amount, b.created, b.updated ORDER BY b.amount DESC";

        return (await con.QueryAsync<Entities.Balance>(query, new { poolId, minimum }))
            .Select(mapper.Map<Balance>)
            .ToArray();
    }

    public async Task<Balance[]> GetPoolBalancesOverThresholdAsync(IDbConnection con, string poolId, decimal minimum, int recordLimit)
    {
        logger.LogInvoke();

        const string query = "SELECT b.poolid, b.address, b.amount, b.created, b.updated, MAX(p.created) AS paiddate FROM balances AS b " +
                                "LEFT JOIN payments AS p ON  p.address = b.address AND p.poolid = b.poolid " +
                                "WHERE b.poolid = @poolId AND b.amount >= @minimum " +
                                "GROUP BY b.poolid, b.address, b.amount, b.created, b.updated ORDER BY b.amount DESC LIMIT @recordLimit;";

        return (await con.QueryAsync<Entities.Balance>(query, new { poolId, minimum, recordLimit }))
            .Select(mapper.Map<Balance>)
            .ToArray();
    }

    public async Task<List<BalanceSummary>> GetTotalBalanceSum(IDbConnection con, string poolId, decimal minimum)
    {
        logger.LogInvoke();

        const string query = @"SELECT days_old AS NoOfDaysOld, COUNT(address) AS CustomersCount, SUM(amount) AS TotalAmount,
        SUM(over_threshold) AS TotalAmountOverThreshold
        FROM(SELECT *, CASE WHEN DATE_PART('day', now() - updated) >= 90 THEN 90
                            WHEN DATE_PART('day', now() - updated) >= 60 THEN 60
                            WHEN DATE_PART('day', now() - updated) >= 30 THEN 30
                            ELSE 0 END AS days_old,
                        CASE WHEN amount >= @minimum THEN amount ELSE 0 END as over_threshold
        FROM balances WHERE poolid = @poolId ORDER BY updated) AS history
        GROUP BY days_old ORDER BY days_old";

        return (await con.QueryAsync<Entities.BalanceSummary>(query, new { poolId, minimum }))
            .Select(mapper.Map<BalanceSummary>)
            .ToList();
    }
}
