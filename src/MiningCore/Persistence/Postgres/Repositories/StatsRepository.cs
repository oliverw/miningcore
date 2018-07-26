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

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using AutoMapper;
using Dapper;
using MiningCore.Extensions;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Model.Projections;
using MiningCore.Persistence.Repositories;
using MiningCore.Time;
using NBitcoin;
using NLog;
using MinerStats = MiningCore.Persistence.Model.Projections.MinerStats;

namespace MiningCore.Persistence.Postgres.Repositories
{
    public class StatsRepository : IStatsRepository
    {
        public StatsRepository(IMapper mapper, IMasterClock clock)
        {
            this.mapper = mapper;
            this.clock = clock;
        }

        private readonly IMapper mapper;
        private readonly IMasterClock clock;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private static readonly TimeSpan MinerStatsMaxAge = TimeSpan.FromMinutes(20);

        public void InsertPoolStats(IDbConnection con, IDbTransaction tx, PoolStats stats)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.PoolStats>(stats);

            var query = "INSERT INTO poolstats(poolid, connectedminers, poolhashrate, networkhashrate, " +
                        "networkdifficulty, lastnetworkblocktime, blockheight, connectedpeers, sharespersecond, created) " +
                        "VALUES(@poolid, @connectedminers, @poolhashrate, @networkhashrate, @networkdifficulty, " +
                        "@lastnetworkblocktime, @blockheight, @connectedpeers, @sharespersecond, @created)";

            con.Execute(query, mapped, tx);
        }

        public void InsertMinerWorkerPerformanceStats(IDbConnection con, IDbTransaction tx, MinerWorkerPerformanceStats stats)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.MinerWorkerPerformanceStats>(stats);

            if (string.IsNullOrEmpty(mapped.Worker))
                mapped.Worker = string.Empty;

            var query = "INSERT INTO minerstats(poolid, miner, worker, hashrate, sharespersecond, created) " +
                "VALUES(@poolid, @miner, @worker, @hashrate, @sharespersecond, @created)";

            con.Execute(query, mapped, tx);
        }

        public PoolStats GetLastPoolStats(IDbConnection con, string poolId)
        {
            logger.LogInvoke();

            var query = "SELECT * FROM poolstats WHERE poolid = @poolId ORDER BY created DESC FETCH NEXT 1 ROWS ONLY";

            var entity = con.QuerySingleOrDefault<Entities.PoolStats>(query, new { poolId });
            if (entity == null)
                return null;

            return mapper.Map<PoolStats>(entity);
        }

        public decimal GetTotalPoolPayments(IDbConnection con, string poolId)
        {
            logger.LogInvoke();

            var query = "SELECT sum(amount) FROM payments WHERE poolid = @poolId";

            var result = con.ExecuteScalar<decimal>(query, new { poolId });
            return result;
        }

        public PoolStats[] GetPoolPerformanceBetweenHourly(IDbConnection con, string poolId, DateTime start, DateTime end)
        {
            logger.LogInvoke(new []{ poolId });

            var query = "SELECT date_trunc('hour', created) AS created, " +
                        "AVG(poolhashrate) AS poolhashrate, " +
                        "CAST(AVG(connectedminers) AS BIGINT) AS connectedminers " +
                        "FROM poolstats " +
                        "WHERE poolid = @poolId AND created >= @start AND created <= @end " +
                        "GROUP BY date_trunc('hour', created) " +
                        "ORDER BY created;";

            return con.Query<Entities.PoolStats>(query, new { poolId, start, end })
                .Select(mapper.Map<PoolStats>)
                .ToArray();
        }

        public MinerStats GetMinerStats(IDbConnection con, IDbTransaction tx, string poolId, string miner)
        {
            logger.LogInvoke(new[] { poolId, miner });

#if true
            var query = "SELECT (SELECT SUM(difficulty) FROM shares WHERE poolid = @poolId AND miner = @miner) AS pendingshares, " +
                        "(SELECT amount FROM balances WHERE poolid = @poolId AND address = @miner) AS pendingbalance, " +
                        "(SELECT SUM(amount) FROM payments WHERE poolid = @poolId and address = @miner) as totalpaid";
#else
            var query = "SELECT (SELECT SUM(sharesaccumulated) FROM minerstats_pre_agg WHERE poolid = @poolId AND miner = @miner) AS pendingshares, " +
                        "(SELECT amount FROM balances WHERE poolid = @poolId AND address = @miner) AS pendingbalance, " +
                        "(SELECT SUM(amount) FROM payments WHERE poolid = @poolId and address = @miner) as totalpaid";
#endif
            var result = con.QuerySingleOrDefault<MinerStats>(query, new { poolId, miner }, tx);

            if (result != null)
            {
                query = "SELECT * FROM payments WHERE poolid = @poolId AND address = @miner" +
                    " ORDER BY created DESC LIMIT 1";

                result.LastPayment = con.QuerySingleOrDefault<Payment>(query, new { poolId, miner }, tx);

                // query timestamp of last stats update
                query = "SELECT created FROM minerstats WHERE poolid = @poolId AND miner = @miner" +
                    " ORDER BY created DESC LIMIT 1";

                var lastUpdate = con.QuerySingleOrDefault<DateTime?>(query, new { poolId, miner }, tx);

                // ignore stale minerstats
                if (lastUpdate.HasValue && (clock.Now - DateTime.SpecifyKind(lastUpdate.Value, DateTimeKind.Utc) > MinerStatsMaxAge))
                    lastUpdate = null;

                if (lastUpdate.HasValue)
                {
                    // load rows rows by timestamp
                    query = "SELECT * FROM minerstats WHERE poolid = @poolId AND miner = @miner AND created = @created";

                    var stats = con.Query<Entities.MinerWorkerPerformanceStats>(query, new { poolId, miner, created = lastUpdate })
                        .Select(mapper.Map<MinerWorkerPerformanceStats>)
                        .ToArray();

                    if (stats.Any())
                    {
                        // replace null worker with empty string
                        foreach(var stat in stats)
                        {
                            if (stat.Worker == null)
                            {
                                stat.Worker = string.Empty;
                                break;
                            }
                        }

                        // transform to dictionary
                        result.Performance = new WorkerPerformanceStatsContainer
                        {
                            Workers = stats.ToDictionary(x => x.Worker ?? string.Empty, x => new WorkerPerformanceStats
                            {
                                Hashrate = x.Hashrate,
                                SharesPerSecond = x.SharesPerSecond
                            }),

                            Created = stats.First().Created
                        };
                    }
                }
            }

            return result;
        }

        public WorkerPerformanceStatsContainer[] GetMinerPerformanceBetweenHourly(IDbConnection con, string poolId, string miner, DateTime start, DateTime end)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT worker, date_trunc('hour', created) AS created, AVG(hashrate) AS hashrate, " +
                        "AVG(sharespersecond) AS sharespersecond FROM minerstats " +
                        "WHERE poolid = @poolId AND miner = @miner AND created >= @start AND created <= @end " +
                        "GROUP BY date_trunc('hour', created), worker " +
                        "ORDER BY created, worker;";

            var entities = con.Query<Entities.MinerWorkerPerformanceStats>(query, new { poolId, miner, start, end })
                .ToArray();

            // ensure worker is not null
            foreach (var entity in entities)
                entity.Worker = entity.Worker ?? string.Empty;

            // group
            var entitiesByDate = entities
                .GroupBy(x=> x.Created);

            var tmp = entitiesByDate.Select(x => new WorkerPerformanceStatsContainer
            {
                Created = x.Key,
                Workers = x.ToDictionary(y => y.Worker ?? string.Empty, y => new WorkerPerformanceStats
                {
                    Hashrate = y.Hashrate,
                    SharesPerSecond = y.SharesPerSecond
                })
            })
            .ToArray();
            //.ToDictionary(x=> x.Created.ToUniversalTime().ToUnixTimestamp(), x=> x);

            //// fill in blanks
            //var result = new List<WorkerPerformanceStatsContainer>();

            //for (var i = 0; i < 24; i++)
            //{
            //    if(tmp.TryGetValue(end.ToUnixTimestamp(), out var item))
            //        result.Insert(0, item);
            //    else
            //        result.Add(new WorkerPerformanceStatsContainer { Created = end, Workers = new Dictionary<string, WorkerPerformanceStats>() });

            //    end = end.AddHours(-1);
            //}

            //return result.ToArray();
            return tmp;
        }

        public WorkerPerformanceStatsContainer[] GetMinerPerformanceBetweenDaily(IDbConnection con, string poolId, string miner, DateTime start, DateTime end)
        {
            logger.LogInvoke(new[] { poolId });

            var query = "SELECT worker, date_trunc('day', created) AS created, AVG(hashrate) AS hashrate, " +
                "AVG(sharespersecond) AS sharespersecond FROM minerstats " +
                "WHERE poolid = @poolId AND miner = @miner AND created >= @start AND created <= @end " +
                "GROUP BY date_trunc('day', created), worker " +
                "ORDER BY created, worker;";

            var entitiesByDate = con.Query<Entities.MinerWorkerPerformanceStats>(query, new { poolId, miner, start, end })
                .ToArray()
                .GroupBy(x => x.Created);

            var tmp = entitiesByDate.Select(x => new WorkerPerformanceStatsContainer
            {
                Created = x.Key,
                Workers = x.ToDictionary(y => y.Worker, y => new WorkerPerformanceStats
                {
                    Hashrate = y.Hashrate,
                    SharesPerSecond = y.SharesPerSecond
                })
            })
            .ToArray();
            //.ToDictionary(x => x.Created.ToUniversalTime().ToUnixTimestamp(), x => x);

            //// fill in blanks
            //var result = new List<WorkerPerformanceStatsContainer>();

            //for (var i = 0; i < 30; i++)
            //{
            //    if (tmp.TryGetValue(end.ToUnixTimestamp(), out var item))
            //        result.Insert(0, item);
            //    else
            //        result.Add(new WorkerPerformanceStatsContainer { Created = end, Workers = new Dictionary<string, WorkerPerformanceStats>() });

            //    end = end.AddDays(-1);
            //}

            //return result.ToArray();
            return tmp;
        }

        public MinerWorkerPerformanceStats[] PagePoolMinersByHashrate(IDbConnection con, string poolId, DateTime from, int page, int pageSize)
        {
            logger.LogInvoke(new[] { (object) poolId, from, page, pageSize });

            var query = "WITH tmp AS " +
                        "( " +
                        "	SELECT  " +
                        "		ms.miner,  " +
                        "		ms.hashrate,  " +
                        "		ms.sharespersecond,  " +
                        "		ROW_NUMBER() OVER(PARTITION BY ms.miner ORDER BY ms.hashrate DESC) AS rk  " +
                        "	FROM (SELECT miner, SUM(hashrate) AS hashrate, SUM(sharespersecond) AS sharespersecond " +
                        "       FROM minerstats " +
                        "       WHERE poolid = @poolid AND created >= @from GROUP BY miner, created) ms " +
                        ") " +
                        "SELECT t.miner, t.hashrate, t.sharespersecond " +
                        "FROM tmp t " +
                        "WHERE t.rk = 1 " +
                        "ORDER by t.hashrate DESC " +
                        "OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return con.Query<Entities.MinerWorkerPerformanceStats>(query, new { poolId, from, offset = page * pageSize, pageSize })
                .Select(mapper.Map<MinerWorkerPerformanceStats>)
                .ToArray();
        }
    }
}
