using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;
using NLog;
using Polly;

namespace Miningcore.Mining
{
    public class StatsRecorder
    {
        public StatsRecorder(IComponentContext ctx,
            IMasterClock clock,
            IConnectionFactory cf,
            IMessageBus messageBus,
            IMapper mapper,
            IShareRepository shareRepo,
            IStatsRepository statsRepo)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));

            this.ctx = ctx;
            this.clock = clock;
            this.cf = cf;
            this.mapper = mapper;
            this.messageBus = messageBus;
            this.shareRepo = shareRepo;
            this.statsRepo = statsRepo;

            BuildFaultHandlingPolicy();
        }

        private readonly IMasterClock clock;
        private readonly IStatsRepository statsRepo;
        private readonly IConnectionFactory cf;
        private readonly IMapper mapper;
        private readonly IMessageBus messageBus;
        private readonly IComponentContext ctx;
        private readonly IShareRepository shareRepo;
        private readonly AutoResetEvent stopEvent = new AutoResetEvent(false);
        private readonly ConcurrentDictionary<string, IMiningPool> pools = new ConcurrentDictionary<string, IMiningPool>();
        private const int StatsInterval = 1; // minutes
        private const int StatsCleanupInterval = 24; // hours
        private const int HashrateCalculationWindow = 1200; // seconds
        private const int MinHashrateCalculationWindow = 30; // seconds
        private const double HashrateBoostFactor = 1.1d;
        private ClusterConfig clusterConfig;
        private Thread thread1;
        private const int RetryCount = 4;
        private IAsyncPolicy readFaultPolicy;

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        #region API-Surface

        public void Configure(ClusterConfig clusterConfig)
        {
            this.clusterConfig = clusterConfig;
        }

        public void AttachPool(IMiningPool pool)
        {
            pools[pool.Config.Id] = pool;
        }

        public void Start()
        {
            logger.Info(() => "Online");

            thread1 = new Thread(async () =>
            {
                // warm-up delay
                Thread.Sleep(TimeSpan.FromSeconds(10));

                var poolStatsInterval = TimeSpan.FromMinutes(StatsInterval);
                var performStatsGcInterval = DateTime.UtcNow;

                while (true)
                {
                    try
                    {
                        await UpdatePoolHashratesAsync();

Console.WriteLine("[DEBUG] Next Stats cleanup at " + performStatsGcInterval);

                        if (clock.UtcNow >= performStatsGcInterval)
                        {
                            await PerformStatsGcAsync();
                            performStatsGcInterval = DateTime.UtcNow.AddHours(StatsCleanupInterval);
                        }
                    }

                    catch(Exception ex)
                    {
                        logger.Error(ex);
                    }

                    var waitResult = stopEvent.WaitOne(poolStatsInterval);

                    // check if stop was signalled
                    if (waitResult)
                        break;
                }
            });

            thread1.Name = "StatsRecorder";
            thread1.Start();
        }

        public void Stop()
        {
            logger.Info(() => "Stopping ..");

            stopEvent.Set();
            thread1.Join();

            logger.Info(() => "Stopped");
        }

        #endregion // API-Surface

        private async Task UpdatePoolHashratesAsync()
        {
            var start = clock.UtcNow;
            var target = start.AddSeconds(-HashrateCalculationWindow);

            var stats = new MinerWorkerPerformanceStats
            {
                Created = start
            };

            foreach(var poolId in pools.Keys)
            {
                stats.PoolId = poolId;

                logger.Info(() => $"Updating hashrates for pool {poolId}");

                var pool = pools[poolId];

                // fetch stats
                var result = await readFaultPolicy.ExecuteAsync(() =>
                    cf.Run(con => shareRepo.GetHashAccumulationBetweenCreatedAsync(con, poolId, target, start)));

                var byMiner = result.GroupBy(x => x.Miner).ToArray();

Console.WriteLine("[DEBUG] Stats length is: " + result.Length);

                // calculate & update pool, connected workers & hashrates
                if (result.Length > 0)
                {
                    double poolHashrate = 0;

                    pool.PoolStats.ConnectedMiners = byMiner.Length;    // update connected miners
                    var windowActual = (result.Max(x => x.LastShare) - result.Min(x => x.FirstShare)).TotalSeconds;   // get share windows time

                    if (windowActual >= MinHashrateCalculationWindow)
                    {
                        var poolHashesAccumulated = result.Sum(x => x.Sum);
                        var poolHashesCountAccumulated = result.Sum(x => x.Count);
                        poolHashrate = pool.HashrateFromShares(poolHashesAccumulated, windowActual) * HashrateBoostFactor;

                        // update PoolHashrate
                        pool.PoolStats.PoolHashrate = (ulong) Math.Ceiling(poolHashrate);
                        pool.PoolStats.SharesPerSecond = (int) (poolHashesCountAccumulated / windowActual);
                    }
                    else
                    {
Console.WriteLine("[DEBUG] Minimum stats windows not met. No shares for the last " + MinHashrateCalculationWindow + " sec");
Console.WriteLine("[DEBUG] windowsActual: " + windowActual);
Console.WriteLine("[DEBUG] MinCalcWindow: " + MinHashrateCalculationWindow);

                        pool.PoolStats.ConnectedMiners = 0;
                        pool.PoolStats.PoolHashrate = 0;
                        pool.PoolStats.SharesPerSecond = 0;
                    }

                    messageBus.NotifyHashrateUpdated(pool.Config.Id, poolHashrate);

                }

                // persist
                await cf.RunTx(async (con, tx) =>
                {
                    var mapped = new Persistence.Model.PoolStats
                    {
                        PoolId = poolId,
                        Created = start
                    };

                    mapper.Map(pool.PoolStats, mapped);
                    mapper.Map(pool.NetworkStats, mapped);    // ToDo: Get Network hashrate

                    await statsRepo.InsertPoolStatsAsync(con, tx, mapped);
                });

                if (result.Length == 0)
                    continue;

                // calculate & update miner, worker hashrates
                foreach(var minerHashes in byMiner)
                {
                    double minerTotalHashrate = 0;

                    await cf.RunTx(async (con, tx) =>
                    {
                        stats.Miner = minerHashes.Key;

                        foreach(var item in minerHashes)
                        {
                            // calculate miner/worker stats
                            var windowActual = (minerHashes.Max(x => x.LastShare) - minerHashes.Min(x => x.FirstShare)).TotalSeconds;

                            if (windowActual >= MinHashrateCalculationWindow)
                            {
                                var hashrate = pool.HashrateFromShares(item.Sum, windowActual) * HashrateBoostFactor;
                                minerTotalHashrate += hashrate;

                                // update
                                stats.Hashrate = hashrate;
                                stats.Worker = item.Worker;
                                stats.SharesPerSecond = (double) item.Count / windowActual;

                                // persist
                                await statsRepo.InsertMinerWorkerPerformanceStatsAsync(con, tx, stats);

                                messageBus.NotifyHashrateUpdated(pool.Config.Id, hashrate, stats.Miner, item.Worker);
                            }
                        }
                    });

                    messageBus.NotifyHashrateUpdated(pool.Config.Id, minerTotalHashrate, stats.Miner, null);
                }
            }
        }

        private async Task PerformStatsGcAsync()
        {
            logger.Info(() => $"Performing stats cleanup");

            await cf.Run(async con =>
            {
                var cutOff = DateTime.UtcNow.AddMonths(-3);

                var rowCount = await statsRepo.DeletePoolStatsBeforeAsync(con, cutOff);
                if(rowCount > 0)
                    logger.Info(() => $"Deleted {rowCount} old poolstats records");

                rowCount = await statsRepo.DeleteMinerStatsBeforeAsync(con, cutOff);
                if (rowCount > 0)
                    logger.Info(() => $"Deleted {rowCount} old minerstats records");
            });

            logger.Info(() => $"Stats cleanup complete");
        }

        private void BuildFaultHandlingPolicy()
        {
            var retry = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .RetryAsync(RetryCount, OnPolicyRetry);

            readFaultPolicy = retry;
        }

        private static void OnPolicyRetry(Exception ex, int retry, object context)
        {
            logger.Warn(() => $"Retry {retry} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }
    }
}
