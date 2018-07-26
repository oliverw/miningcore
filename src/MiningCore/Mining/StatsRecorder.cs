using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using Autofac;
using AutoMapper;
using MiningCore.Configuration;
using MiningCore.Contracts;
using MiningCore.Extensions;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using MiningCore.Time;
using NLog;
using Polly;

namespace MiningCore.Mining
{
    public class StatsRecorder
    {
        public StatsRecorder(IComponentContext ctx,
            IMasterClock clock,
            IConnectionFactory cf,
            IMapper mapper,
            IShareRepository shareRepo,
            IStatsRepository statsRepo)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));

            this.ctx = ctx;
            this.clock = clock;
            this.cf = cf;
            this.mapper = mapper;
            this.shareRepo = shareRepo;
            this.statsRepo = statsRepo;

            BuildFaultHandlingPolicy();
        }

        private readonly IMasterClock clock;
        private readonly IStatsRepository statsRepo;
        private readonly IConnectionFactory cf;
        private readonly IMapper mapper;
        private readonly IComponentContext ctx;
        private readonly IShareRepository shareRepo;
        private readonly AutoResetEvent stopEvent = new AutoResetEvent(false);
        private readonly ConcurrentDictionary<string, IMiningPool> pools = new ConcurrentDictionary<string, IMiningPool>();
        private const int HashrateCalculationWindow = 1200;  // seconds
        private const int MinHashrateCalculationWindow = 300;  // seconds
        private const double HashrateBoostFactor = 1.07d;
        private ClusterConfig clusterConfig;
        private Thread thread1;
        private const int RetryCount = 4;
        private Policy readFaultPolicy;

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

            thread1 = new Thread(() =>
            {
                // warm-up delay
                Thread.Sleep(TimeSpan.FromSeconds(10));

                var interval = TimeSpan.FromMinutes(5);

                while (true)
                {
                    try
                    {
                        UpdatePoolHashrates();
                    }

                    catch (Exception ex)
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

        private void UpdatePoolHashrates()
        {
            var start = clock.Now;
            var target = start.AddSeconds(-HashrateCalculationWindow);

            var stats = new MinerWorkerPerformanceStats
            {
                Created = start
            };

            var poolIds = pools.Keys;

            foreach (var poolId in poolIds)
            {
                stats.PoolId = poolId;

                logger.Info(() => $"Updating hashrates for pool {poolId}");

                var pool = pools[poolId];

                // fetch stats
                var result = readFaultPolicy.Execute(() =>
                    cf.Run(con => shareRepo.GetHashAccumulationBetweenCreated(con, poolId, target, start)));

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
                    }
                }

                // persist
                cf.RunTx((con, tx) =>
                {
                    var mapped = new Persistence.Model.PoolStats
                    {
                        PoolId = poolId,
                        Created = start
                    };

                    mapper.Map(pool.PoolStats, mapped);
                    mapper.Map(pool.NetworkStats, mapped);

                    statsRepo.InsertPoolStats(con, tx, mapped);
                });

                if (result.Length == 0)
                    continue;

                // calculate & update miner, worker hashrates
                foreach (var minerHashes in byMiner)
                {
                    cf.RunTx((con, tx) =>
                    {
                        stats.Miner = minerHashes.Key;

                        foreach (var item in minerHashes)
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
                                statsRepo.InsertMinerWorkerPerformanceStats(con, tx, stats);
                            }
                        }
                    });
                }
            }
        }

        private void BuildFaultHandlingPolicy()
        {
            var retry = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .Retry(RetryCount, OnPolicyRetry);

            readFaultPolicy = retry;
        }

        private static void OnPolicyRetry(Exception ex, int retry, object context)
        {
            logger.Warn(() => $"Retry {retry} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }
    }
}
