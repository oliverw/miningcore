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
        private const int HashrateCalculationWindow = 1200; // seconds
        private const int MinHashrateCalculationWindow = 300; // seconds
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

                var interval = TimeSpan.FromMinutes(5);

                while(true)
                {
                    try
                    {
                        await UpdatePoolHashratesAsync();
                        await PerformStatsGcAsync();
                    }

                    catch(Exception ex)
                    {
                        logger.Error(ex);
                    }

                    var waitResult = stopEvent.WaitOne(interval);

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
            var start = clock.Now;
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

                if (result.Length > 0)
                {
                    // calculate pool stats
                    var windowActual = (result.Max(x => x.LastShare) - result.Min(x => x.FirstShare)).TotalSeconds;

                    if (windowActual >= MinHashrateCalculationWindow)
                    {
                        var poolHashesAccumulated = result.Sum(x => x.Sum);
                        var poolHashesCountAccumulated = result.Sum(x => x.Count);
                        var poolHashrate = pool.HashrateFromShares(poolHashesAccumulated, windowActual) * HashrateBoostFactor;

                        // update
                        pool.PoolStats.ConnectedMiners = byMiner.Length;
                        pool.PoolStats.PoolHashrate = (ulong) Math.Ceiling(poolHashrate);
                        pool.PoolStats.SharesPerSecond = (int) (poolHashesCountAccumulated / windowActual);

                        messageBus.NotifyHashrateUpdated(pool.Config.Id, poolHashrate);
                    }
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
                    mapper.Map(pool.NetworkStats, mapped);

                    await statsRepo.InsertPoolStatsAsync(con, tx, mapped);
                });

                if (result.Length == 0)
                    continue;

                // calculate & update miner, worker hashrates
                foreach(var minerHashes in byMiner)
                {
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
                }
            }
        }

        private async Task PerformStatsGcAsync()
        {
            logger.Info(() => $"Performing Stats GC");

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

            logger.Info(() => $"Stats GC complete");
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
