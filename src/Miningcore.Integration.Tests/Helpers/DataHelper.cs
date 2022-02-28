using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Miningcore.Configuration;
using Miningcore.Integration.Tests.Data;
using Miningcore.Persistence.Model;

namespace Miningcore.Integration.Tests.Helpers
{
    public class DataHelper
    {
        private const string PoolId = "eth1";
        private const string Miner1 = "0x68E16e7e0f07686fa1EaBBcA2f37FACAD23C6D31";
        private readonly PostgresDataRepository dataRepository;

        public DataHelper(PersistenceConfig config)
        {
            var connectionString = $"Server={config.Postgres.Host};Port={config.Postgres.Port};Database={config.Postgres.Database};User Id={config.Postgres.User};Password={config.Postgres.Password};Timeout=60;CommandTimeout=60;Keepalive=60;";

            if(config.Postgres.Pooling != null)
                connectionString += $"Pooling=true;Minimum Pool Size={config.Postgres.Pooling.MinPoolSize};Maximum Pool Size={(config.Postgres.Pooling.MaxPoolSize > 0 ? config.Postgres.Pooling.MaxPoolSize : 100)};";

            if(config.Postgres.Ssl)
                connectionString += "SSL Mode=Require;Trust Server Certificate=True;Server Compatibility Mode=Redshift;";

            dataRepository = new PostgresDataRepository(connectionString);
        }

        public async Task AddPoolStateAsync()
        {
            await dataRepository.AddOrUpdatePoolState(new PoolState
            {
                HashValue = 0.0000000001008236701671350m,
                LastPayout = DateTime.UtcNow.AddMinutes(-10),
                PoolId = PoolId
            });
        }

        public async Task AddPoolStatisticsAsync()
        {
            await dataRepository.AddPoolStatsAsync(new PoolStats
            {
                PoolId = PoolId,
                ConnectedMiners = 1,
                ConnectedPeers = 4,
                BlockHeight = 13805497,
                LastNetworkBlockTime = DateTime.UtcNow.AddMinutes(-10),
                NetworkDifficulty = 1.19592924628282e+016,
                NetworkHashrate = 920852200900687,
                PoolHashrate = 250692370432,
                SharesPerSecond = 86,
                Created = DateTime.UtcNow
            });
        }

        public async Task AddTestSharesAsync()
        {
            await dataRepository.AddShares(new List<Share>
            {
                new()
                {
                    BlockHeight = 13767397, Difficulty = 6078203174.465, NetworkDifficulty = 1.20076595987043e+016, Miner = Miner1,
                    Worker = "0", UserAgent = "nsfminer-1.3.14", IpAddress = "24.62.62.134", Source = "", Created = DateTime.UtcNow, Accepted = DateTime.UtcNow,
                    PoolId = PoolId
                },
                new()
                {
                    BlockHeight = 13767328, Difficulty = 429496729.6, NetworkDifficulty = 1.19947687790251e+016, Miner = Miner1,
                    Worker = "0", UserAgent = "nsfminer-1.3.14", IpAddress = "24.62.62.134", Source = "", Created = DateTime.UtcNow, Accepted = DateTime.UtcNow,
                    PoolId = PoolId
                },
                new()
                {
                    BlockHeight = 13767333, Difficulty = 429496729.6, NetworkDifficulty = 1.20078517505037e+016, Miner = Miner1,
                    Worker = "0", UserAgent = "nsfminer-1.3.14", IpAddress = "24.62.62.134", Source = "", Created = DateTime.UtcNow, Accepted = DateTime.UtcNow,
                    PoolId = PoolId
                },
                new()
                {
                    BlockHeight = 13767334, Difficulty = 429496729.6, NetworkDifficulty = 1.20081236313635e+016, Miner = Miner1,
                    Worker = "0", UserAgent = "nsfminer-1.3.14", IpAddress = "24.62.62.134", Source = "", Created = DateTime.UtcNow, Accepted = DateTime.UtcNow,
                    PoolId = PoolId
                }
            });
        }

        public async Task<long> GetUnProcessedSharesCountAsync()
        {
            return await dataRepository.GetUnProcessedSharesCountAsync(PoolId);
        }

        public async Task<Balance> GetBalanceAsync(string miner = Miner1)
        {
            return await dataRepository.GetBalanceAsync(PoolId, miner);
        }

        public async Task CleanupShares()
        {
            await dataRepository.Cleanup(new List<string>
            {
                "shares",
                "balances",
                "poolstate",
                "poolstats"
            });
        }
    }
}
