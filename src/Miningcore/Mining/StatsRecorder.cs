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
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, IMiningPool> pools = new ConcurrentDictionary<string, IMiningPool>();
        private readonly TimeSpan interval = TimeSpan.FromMinutes(5);
        private const int HashrateCalculationWindow = 1200; // seconds
        private const int MinHashrateCalculationWindow = 300; // seconds
        private const double HashrateBoostFactor = 1.1d;
        private ClusterConfig clusterConfig;
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
            Task.Run(async () =>
            {
                logger.Info(() => "Online");

                // warm-up delay
                await Task.Delay(TimeSpan.FromSeconds(10));

                while(!cts.IsCancellationRequested)
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

                    await Task.Delay(interval, cts.Token);
                }
            });
        }

        public void Stop()
        {
            logger.Info(() => "Stopping ..");

            cts.Cancel();

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

                if(result.Length > 0)
                {
                    // calculate pool stats
                    var windowActual = (result.Max(x => x.LastShare) - result.Min(x => x.FirstShare)).TotalSeconds;

                    if(windowActual >= MinHashrateCalculationWindow)
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

                else
                {
                    // reset
                    pool.PoolStats.ConnectedMiners = 0;
                    pool.PoolStats.PoolHashrate = 0;
                    pool.PoolStats.SharesPerSecond = 0;

                    messageBus.NotifyHashrateUpdated(pool.Config.Id, 0);

                    logger.Info(() => $"Reset performance stats for pool {poolId}");
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

                if(result.Length == 0)
                    continue;

                // retrieve most recent miner/worker hashrate sample, if non-zero
                var previousMinerWorkerHashrates = await cf.Run(async (con) =>
                {
                    return await statsRepo.GetPoolMinerWorkerHashratesAsync(con, poolId);
                });

                string buildKey(string miner, string worker = null)
                {
                    return !string.IsNullOrEmpty(worker) ? $"{miner}:{worker}" : miner;
                }

                var previousNonZeroMinerWorkers = new HashSet<string>(
                    previousMinerWorkerHashrates.Select(x => buildKey(x.Miner, x.Worker)));

                var currentNonZeroMinerWorkers = new HashSet<string>();

                // calculate & update miner, worker hashrates
                foreach(var minerHashes in byMiner)
                {
                    double minerTotalHashrate = 0;

                    await cf.RunTx(async (con, tx) =>
                    {
                        stats.Miner = minerHashes.Key;

                        // book keeping
                        currentNonZeroMinerWorkers.Add(buildKey(stats.Miner));

                        foreach(var item in minerHashes)
                        {
                            // calculate miner/worker stats
                            var windowActual = (minerHashes.Max(x => x.LastShare) - minerHashes.Min(x => x.FirstShare)).TotalSeconds;

                            if(windowActual >= MinHashrateCalculationWindow)
                            {
                                var hashrate = pool.HashrateFromShares(item.Sum, windowActual) * HashrateBoostFactor;
                                minerTotalHashrate += hashrate;

                                // update
                                stats.Hashrate = hashrate;
                                stats.Worker = item.Worker;
                                stats.SharesPerSecond = (double) item.Count / windowActual;

                                // persist
                                await statsRepo.InsertMinerWorkerPerformanceStatsAsync(con, tx, stats);

                                // broadcast
                                messageBus.NotifyHashrateUpdated(pool.Config.Id, hashrate, stats.Miner, item.Worker);

                                // book keeping
                                currentNonZeroMinerWorkers.Add(buildKey(stats.Miner, stats.Worker));
                            }
                        }
                    });

                    messageBus.NotifyHashrateUpdated(pool.Config.Id, minerTotalHashrate, stats.Miner, null);
                }

                // identify and reset "orphaned" hashrates
                var orphanedHashrateForMinerWorker = previousNonZeroMinerWorkers.Except(currentNonZeroMinerWorkers).ToArray();

                await cf.RunTx(async (con, tx) =>
                {
                    // reset
                    stats.Hashrate = 0;
                    stats.SharesPerSecond = 0;

                    foreach(var item in orphanedHashrateForMinerWorker)
                    {
                        var parts = item.Split(":");
                        var miner = parts[0];
                        var worker = parts.Length > 1 ? parts[1] : null;

                        stats.Miner = parts[0];
                        stats.Worker = worker;

                        // persist
                        await statsRepo.InsertMinerWorkerPerformanceStatsAsync(con, tx, stats);

                        // broadcast
                        messageBus.NotifyHashrateUpdated(pool.Config.Id, 0, stats.Miner, stats.Worker);

                        if(string.IsNullOrEmpty(stats.Worker))
                            logger.Info(() => $"Reset performance stats for miner {stats.Miner} on pool {poolId}");
                        else
                            logger.Info(() => $"Reset performance stats for worker {stats.Worker} of miner {stats.Miner} on pool {poolId}");
                    }
                });
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
                if(rowCount > 0)
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
