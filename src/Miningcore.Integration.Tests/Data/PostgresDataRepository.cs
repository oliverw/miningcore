using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Miningcore.Persistence.Model;
using Npgsql;
using NpgsqlTypes;

namespace Miningcore.Integration.Tests.Data
{
    public class PostgresDataRepository
    {
        private readonly string connectionString;
        public PostgresDataRepository(string conStr)
        {
            connectionString = conStr;
        }

        public async Task<NpgsqlConnection> OpenConnectionAsync()
        {
            var con = new NpgsqlConnection(connectionString);
            await con.OpenAsync();
            return con;
        }

        public async Task Cleanup(List<string> tables)
        {
            await using var con = await OpenConnectionAsync();
            var sql = string.Join("\n", tables.Select(t => $"TRUNCATE TABLE {t};"));

            await con.ExecuteAsync(sql);
        }

        public async Task AddShares(List<Share> shares)
        {
            await using var con = await OpenConnectionAsync();
            const string sql = "COPY shares (poolid, blockheight, difficulty, " +
                                 "networkdifficulty, miner, worker, useragent, ipaddress, source, created, accepted) FROM STDIN (FORMAT BINARY)";

            await using var writer = con.BeginBinaryImport(sql);
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
                await writer.WriteAsync(DateTime.UtcNow, NpgsqlDbType.TimestampTz);
            }

            await writer.CompleteAsync();
            await writer.DisposeAsync();
        }

        public async Task AddOrUpdatePoolState(PoolState state)
        {
            await using var con = await OpenConnectionAsync();

            const string sql = @"INSERT INTO poolstate as ps(poolid, hashvalue, lastpayout) VALUES (@poolId, @hashValue, @lastpayout)
                ON CONFLICT (poolid)
                DO UPDATE SET hashvalue = (CASE WHEN EXCLUDED.hashvalue ISNULL OR EXCLUDED.hashvalue=0 THEN ps.hashvalue ELSE EXCLUDED.hashvalue END), lastpayout = COALESCE(EXCLUDED.lastpayout, ps.lastpayout)";

            await con.ExecuteAsync(sql, new DynamicParameters(state));
        }

        public async Task AddPoolStatsAsync(PoolStats stats)
        {
            await using var con = await OpenConnectionAsync();

            const string sql = "INSERT INTO poolstats(poolid, connectedminers, poolhashrate, networkhashrate, " +
                                 "networkdifficulty, lastnetworkblocktime, blockheight, connectedpeers, sharespersecond, created) " +
                                 "VALUES(@poolid, @connectedminers, @poolhashrate, @networkhashrate, @networkdifficulty, " +
                                 "@lastnetworkblocktime, @blockheight, @connectedpeers, @sharespersecond, @created)";

            await con.ExecuteAsync(sql, new DynamicParameters(stats));
        }

        public async Task<long> GetUnProcessedSharesCountAsync(string poolId)
        {
            await using var con = await OpenConnectionAsync();
            const string sql = "SELECT count(*) FROM shares WHERE poolid = @poolId AND processed is NULL;";

            return await con.QuerySingleAsync<long>(sql, new { poolId });
        }

        public async Task<Balance> GetBalanceAsync(string poolId, string miner)
        {
            await using var con = await OpenConnectionAsync();
            const string sql = "SELECT * FROM balances WHERE poolid = @poolId AND address = @miner;";

            return (await con.QueryAsync<Balance>(sql, new { poolId, miner })).FirstOrDefault();
        }

        public static void GetPayment()
        {

        }

    }
}
