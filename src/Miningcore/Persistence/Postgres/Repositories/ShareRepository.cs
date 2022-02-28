using System.Data;
using AutoMapper;
using Dapper;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;
using Miningcore.Persistence.Repositories;
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

    public async Task BatchInsertAsync(IDbConnection con, IDbTransaction tx, IEnumerable<Share> shares, CancellationToken ct)
    {
        // NOTE: Even though the tx parameter is completely ignored here,
        // the COPY command still honors a current ambient transaction

        var pgCon = (NpgsqlConnection) con;

        const string query = @"COPY shares (poolid, blockheight, difficulty,
            networkdifficulty, miner, worker, useragent, ipaddress, source, created, accepted) FROM STDIN (FORMAT BINARY)";

        await using(var writer = await pgCon.BeginBinaryImportAsync(query, ct))
        {
            foreach(var share in shares)
            {
                await writer.StartRowAsync(ct);

                await writer.WriteAsync(share.PoolId, ct);
                await writer.WriteAsync((long) share.BlockHeight, NpgsqlDbType.Bigint, ct);
                await writer.WriteAsync(share.Difficulty, NpgsqlDbType.Double, ct);
                await writer.WriteAsync(share.NetworkDifficulty, NpgsqlDbType.Double, ct);
                await writer.WriteAsync(share.Miner, ct);
                await writer.WriteAsync(share.Worker, ct);
                await writer.WriteAsync(share.UserAgent, ct);
                await writer.WriteAsync(share.IpAddress, ct);
                await writer.WriteAsync(share.Source, ct);
                await writer.WriteAsync(share.Created, NpgsqlDbType.TimestampTz, ct);
                await writer.WriteAsync(DateTime.UtcNow, NpgsqlDbType.TimestampTz);
            }

            await writer.CompleteAsync(ct);
        }
    }

    public async Task<Share[]> ReadSharesBeforeCreatedAsync(IDbConnection con, string poolId, DateTime before,
        bool inclusive, int pageSize, CancellationToken ct)
    {
        var query = @$"SELECT * FROM shares WHERE poolid = @poolId AND created {(inclusive ? " <= " : " < ")} @before
            ORDER BY created DESC FETCH NEXT @pageSize ROWS ONLY";

        return (await con.QueryAsync<Entities.Share>(new CommandDefinition(query, new { poolId, before, pageSize }, cancellationToken: ct)))
            .Select(mapper.Map<Share>)
            .ToArray();
    }

    public Task<long> CountSharesBeforeCreatedAsync(IDbConnection con, IDbTransaction tx, string poolId, DateTime before, CancellationToken ct)
    {
        const string query = "SELECT count(*) FROM shares WHERE poolid = @poolId AND created < @before";

        return con.QuerySingleAsync<long>(new CommandDefinition(query, new { poolId, before }, tx, cancellationToken: ct));
    }

    public Task<long> CountSharesByMinerAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, CancellationToken ct)
    {
        const string query = "SELECT count(*) FROM shares WHERE poolid = @poolId AND miner = @miner";

        return con.QuerySingleAsync<long>(new CommandDefinition(query, new { poolId, miner}, tx, cancellationToken: ct));
    }

    public async Task DeleteSharesByMinerAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, CancellationToken ct)
    {
        const string query = "DELETE FROM shares WHERE poolid = @poolId AND miner = @miner";

        await con.ExecuteAsync(new CommandDefinition(query, new { poolId, miner}, tx, cancellationToken: ct));
    }

    public async Task DeleteSharesBeforeCreatedAsync(IDbConnection con, IDbTransaction tx, string poolId, DateTime before, CancellationToken ct)
    {
        const string query = "DELETE FROM shares WHERE poolid = @poolId AND created < @before";

        await con.ExecuteAsync(new CommandDefinition(query, new { poolId, before }, tx, cancellationToken: ct));
    }

    public Task<double?> GetAccumulatedShareDifficultyBetweenCreatedAsync(IDbConnection con, string poolId, DateTime start, DateTime end, CancellationToken ct)
    {
        const string query = "SELECT SUM(difficulty) FROM shares WHERE poolid = @poolId AND created > @start AND created < @end";

        return con.QuerySingleAsync<double?>(new CommandDefinition(query, new { poolId, start, end }, cancellationToken: ct));
    }

    public async Task<MinerWorkerHashes[]> GetHashAccumulationBetweenCreatedAsync(IDbConnection con, string poolId, DateTime start, DateTime end, CancellationToken ct)
    {
        const string query = @"SELECT SUM(difficulty), COUNT(difficulty), MIN(created) AS firstshare, MAX(created) AS lastshare, miner, worker FROM shares
            WHERE poolid = @poolId AND created >= @start AND created <= @end
            GROUP BY miner, worker";

        return (await con.QueryAsync<MinerWorkerHashes>(new CommandDefinition(query, new { poolId, start, end }, cancellationToken: ct)))
            .ToArray();
    }

    public async Task<KeyValuePair<string, double>[]> GetAccumulatedUserAgentShareDifficultyBetweenCreatedAsync(
        IDbConnection con, string poolId, DateTime start, DateTime end, bool byVersion, CancellationToken ct)
    {
        const string query = @"SELECT SUM(difficulty) AS value, REGEXP_REPLACE(useragent, '/.+', '') AS key FROM shares
                WHERE poolid = @poolId AND created > @start AND created < @end
                GROUP BY key ORDER BY value DESC";

        const string queryByVersion = @"SELECT SUM(difficulty) AS value, useragent AS key FROM shares
            WHERE poolid = @poolId AND created > @start AND created < @end
            GROUP BY key ORDER BY value DESC";

        return (await con.QueryAsync<KeyValuePair<string, double>>(new CommandDefinition(!byVersion ? query : queryByVersion, new { poolId, start, end }, cancellationToken: ct)))
            .ToArray();
    }

    public async Task<string[]> GetRecentyUsedIpAddressesAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, CancellationToken ct)
    {
        const string query = @"SELECT DISTINCT s.ipaddress FROM (SELECT * FROM shares
            WHERE poolid = @poolId and miner = @miner ORDER BY CREATED DESC LIMIT 100) s";

        return (await con.QueryAsync<string>(new CommandDefinition(query, new { poolId, miner }, tx, cancellationToken: ct)))
            .ToArray();
    }

    public async Task DeleteProcessedSharesBeforeAcceptedAsync(IDbConnection con, IDbTransaction tx, string poolId, DateTime before)
    {
        const string query = "DELETE FROM shares WHERE poolid = @poolId AND processed is not null AND accepted <= @before";

        await con.ExecuteAsync(query, new { poolId, before }, tx);
    }

    public async Task ProcessSharesForUserBeforeAcceptedAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner, DateTime before)
    {
        const string query = "UPDATE shares SET processed = now() at time zone 'utc' WHERE poolid = @poolId AND miner = @miner AND accepted <= @before";

        await con.ExecuteAsync(query, new { poolId, miner, before }, tx);
    }

    public async Task<Share[]> ReadUnprocessedSharesBeforeAcceptedAsync(IDbConnection con, string poolId, DateTime before, bool inclusive, int pageSize)
    {
        var query = $"SELECT * FROM shares WHERE poolid = @poolId AND processed is NULL AND accepted {(inclusive ? " <= " : " < ")} @before " +
                    "ORDER BY accepted DESC FETCH NEXT (@pageSize) ROWS ONLY";

        return (await con.QueryAsync<Entities.Share>(query, new { poolId, before, pageSize }))
            .Select(mapper.Map<Share>)
            .ToArray();
    }

    public async Task<MinerWorkerHashes[]> GetHashAccumulationBetweenAcceptedAsync(IDbConnection con, string poolId, DateTime start, DateTime end, CancellationToken ct)
    {
        const string query = "SELECT SUM(difficulty), COUNT(difficulty), MIN(accepted) AS firstshare, MAX(accepted) AS lastshare, miner, worker FROM shares " +
            "WHERE poolid = @poolId AND accepted >= @start AND accepted <= @end " +
            "GROUP BY miner, worker";

        return (await con.QueryAsync<MinerWorkerHashes>(new CommandDefinition(query, new { poolId, start, end }, cancellationToken: ct)))
            .ToArray();
    }
}
