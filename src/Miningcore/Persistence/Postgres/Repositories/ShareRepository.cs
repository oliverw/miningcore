using System.Data;
using AutoMapper;
using Dapper;
using Miningcore.Extensions;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;
using Miningcore.Persistence.Repositories;
using NLog;
using Npgsql;
using NpgsqlTypes;

namespace Miningcore.Persistence.Postgres.Repositories;

public class ShareRepository : IShareRepository
{
    public ShareRepository(IMapper mapper)
    {
        this.mapper = mapper;
    }

    private readonly IMapper mapper;
    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

    public async Task InsertAsync(IDbConnection con, IDbTransaction tx, Share share)
    {
        logger.LogInvoke();

        var mapped = mapper.Map<Entities.Share>(share);

        const string query = "INSERT INTO shares(poolid, blockheight, difficulty, " +
            "networkdifficulty, miner, worker, useragent, ipaddress, source, created) " +
            "VALUES(@poolid, @blockheight, @difficulty, " +
            "@networkdifficulty, @miner, @worker, @useragent, @ipaddress, @source, @created)";

        await con.ExecuteAsync(query, mapped, tx);
    }

    public async Task BatchInsertAsync(IDbConnection con, IDbTransaction tx, IEnumerable<Share> shares)
    {
        logger.LogInvoke();

        // NOTE: Even though the tx parameter is completely ignored here,
        // the COPY command still honors a current ambient transaction

        var pgCon = (NpgsqlConnection) con;

        const string query = "COPY shares (poolid, blockheight, difficulty, " +
            "networkdifficulty, miner, worker, useragent, ipaddress, source, created) FROM STDIN (FORMAT BINARY)";

        await using(var writer = pgCon.BeginBinaryImport(query))
        {
            foreach(var share in shares)
            {
                await writer.StartRowAsync();

                await writer.WriteAsync(share.PoolId);
                await writer.WriteAsync((long) share.BlockHeight, NpgsqlDbType.Bigint);
                await writer.WriteAsync(share.Difficulty, NpgsqlDbType.Double);
                await writer.WriteAsync(share.NetworkDifficulty, NpgsqlDbType.Double);
                await writer.WriteAsync(share.Miner);
                await writer.WriteAsync(share.Worker);
                await writer.WriteAsync(share.UserAgent);
                await writer.WriteAsync(share.IpAddress);
                await writer.WriteAsync(share.Source);
                await writer.WriteAsync(share.Created, NpgsqlDbType.TimestampTz);
            }

            await writer.CompleteAsync();
        }
    }

    public async Task<Share[]> ReadSharesBeforeCreatedAsync(IDbConnection con, string poolId, DateTime before, bool inclusive, int pageSize)
    {
        logger.LogInvoke(new object[] { poolId });

        var query = $"SELECT * FROM shares WHERE poolid = @poolId AND created {(inclusive ? " <= " : " < ")} @before " +
            "ORDER BY created DESC FETCH NEXT (@pageSize) ROWS ONLY";

        return (await con.QueryAsync<Entities.Share>(query, new { poolId, before, pageSize }))
            .Select(mapper.Map<Share>)
            .ToArray();
    }

    public async Task<Share[]> ReadSharesBeforeAndAfterCreatedAsync(IDbConnection con, string poolId, DateTime before, DateTime after, bool inclusive, int pageSize)
    {
        logger.LogInvoke(new object[] { poolId });

        var query = $"SELECT * FROM shares WHERE poolid = @poolId AND created {(inclusive ? " <= " : " < ")} @before " +
            $"AND created {(inclusive ? " >= " : " > ")} @after" +
            "ORDER BY created DESC FETCH NEXT (@pageSize) ROWS ONLY";

        return (await con.QueryAsync<Entities.Share>(query, new { poolId, before, after, pageSize }))
            .Select(mapper.Map<Share>)
            .ToArray();
    }

    public async Task<Share[]> PageSharesBetweenCreatedAsync(IDbConnection con, string poolId, DateTime start, DateTime end, int page, int pageSize)
    {
        logger.LogInvoke(new object[] { poolId });

        var query = "SELECT * FROM shares WHERE poolid = @poolId AND created >= @start AND created <= @end " +
            "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

        return (await con.QueryAsync<Entities.Share>(query, new { poolId, start, end, offset = page * pageSize, pageSize }))
            .Select(mapper.Map<Share>)
            .ToArray();
    }

    public Task<long> CountSharesBeforeCreatedAsync(IDbConnection con, IDbTransaction tx, string poolId, DateTime before)
    {
        logger.LogInvoke(new object[] { poolId });

        const string query = "SELECT count(*) FROM shares WHERE poolid = @poolId AND created < @before";

        return con.QuerySingleAsync<long>(query, new { poolId, before }, tx);
    }

    public Task<long> CountSharesByMinerAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner)
    {
        logger.LogInvoke(new object[] { poolId });

        const string query = "SELECT count(*) FROM shares WHERE poolid = @poolId AND miner = @miner";

        return con.QuerySingleAsync<long>(query, new { poolId, miner}, tx);
    }

    public async Task DeleteSharesByMinerAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner)
    {
        logger.LogInvoke(new object[] { poolId });

        const string query = "DELETE FROM shares WHERE poolid = @poolId AND miner = @miner";

        await con.ExecuteAsync(query, new { poolId, miner}, tx);
    }

    public async Task DeleteSharesBeforeCreatedAsync(IDbConnection con, IDbTransaction tx, string poolId, DateTime before)
    {
        logger.LogInvoke(new object[] { poolId });

        const string query = "DELETE FROM shares WHERE poolid = @poolId AND created < @before";

        await con.ExecuteAsync(query, new { poolId, before }, tx);
    }

    public Task<long> CountSharesBetweenCreatedAsync(IDbConnection con, string poolId, string miner, DateTime? start, DateTime? end)
    {
        logger.LogInvoke(new object[] { poolId });

        var whereClause = "poolid = @poolId AND miner = @miner";

        if(start.HasValue)
            whereClause += " AND created >= @start ";
        if(end.HasValue)
            whereClause += " AND created <= @end";

        var query = $"SELECT count(*) FROM shares WHERE {whereClause}";

        return con.QuerySingleAsync<long>(query, new { poolId, miner, start, end });
    }

    public Task<double?> GetAccumulatedShareDifficultyBetweenCreatedAsync(IDbConnection con, string poolId, DateTime start, DateTime end)
    {
        logger.LogInvoke(new object[] { poolId });

        const string query = "SELECT SUM(difficulty) FROM shares WHERE poolid = @poolId AND created > @start AND created < @end";

        return con.QuerySingleAsync<double?>(query, new { poolId, start, end });
    }

    public async Task<MinerWorkerHashes[]> GetAccumulatedShareDifficultyTotalAsync(IDbConnection con, string poolId)
    {
        logger.LogInvoke(new object[] { (object) poolId });

        const string query = "SELECT SUM(difficulty) AS sum, COUNT(difficulty) AS count, miner, worker FROM shares WHERE poolid = @poolid group by miner, worker";

        return (await con.QueryAsync<MinerWorkerHashes>(query, new { poolId }))
            .ToArray();
    }

    public async Task<MinerWorkerHashes[]> GetHashAccumulationBetweenCreatedAsync(IDbConnection con, string poolId, DateTime start, DateTime end)
    {
        logger.LogInvoke(new object[] { poolId });

        const string query = "SELECT SUM(difficulty), COUNT(difficulty), MIN(created) AS firstshare, MAX(created) AS lastshare, miner, worker FROM shares " +
            "WHERE poolid = @poolId AND created >= @start AND created <= @end " +
            "GROUP BY miner, worker";

        return (await con.QueryAsync<MinerWorkerHashes>(query, new { poolId, start, end }))
            .ToArray();
    }

    public async Task<string[]> GetRecentyUsedIpAddresses(IDbConnection con, IDbTransaction tx, string poolId, string miner)
    {
        logger.LogInvoke(new object[] { poolId });

        const string query = "SELECT DISTINCT s.ipaddress FROM (SELECT * FROM shares " +
            "WHERE poolid = @poolId and miner = @miner ORDER BY CREATED DESC LIMIT 100) s";

        return (await con.QueryAsync<string>(query, new { poolId, miner }, tx))
            .ToArray();
    }
}
