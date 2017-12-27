using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly Dictionary<string, IMiningPool> pools = new Dictionary<string, IMiningPool>();
        private const int HashrateCalculationWindow = 1200;  // seconds
        private ClusterConfig clusterConfig;
        private Thread thread;
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
            thread = new Thread(async () =>
            {
                logger.Info(() => "Online");

                // warm-up delay
                await Task.Delay(TimeSpan.FromSeconds(10));

                var interval = TimeSpan.FromMinutes(5);

                while (true)
                {
                    try
                    {
                        await UpdatePoolsAsync();
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

            thread.Name = "StatsRecorder";
            thread.Start();
        }

        public void Stop()
        {
            logger.Info(() => "Stopping ..");

            stopEvent.Set();
            thread.Join();

            logger.Info(() => "Stopped");
        }

        #endregion // API-Surface

        private Task UpdatePoolsAsync()
        {
            UpdateHashrates();

            return Task.FromResult(true);
        }

        private void UpdateHashrates()
        {
            var start = clock.Now;
            var target = start.AddSeconds(-HashrateCalculationWindow);

            var stats = new MinerWorkerPerformanceStats
            {
                Created = start
            };

            foreach (var poolId in pools.Keys)
            {
                stats.PoolId = poolId;

                logger.Info(() => $"Updating hashrates for pool {poolId}");

                var pool = pools[poolId];

                // fetch stats
                var result = readFaultPolicy.Execute(() =>
                    cf.Run(con => shareRepo.GetHashAccumulationBetweenCreated(con, poolId, target, start)));

                if (result.Length == 0)
                    continue;

                var byMiner = result.GroupBy(x => x.Miner).ToArray();

                // calculate pool stats
                var windowActual = Math.Max(1, (result.Max(x => x.LastShare) - result.Min(x => x.FirstShare)).TotalSeconds);
                var poolHashesAccumulated = result.Sum(x => x.Sum);
                var poolHashesCountAccumulated = result.Sum(x => x.Count);
                var poolHashrate = pool.HashrateFromShares(poolHashesAccumulated, windowActual);

                // update
                pool.PoolStats.ConnectedMiners = byMiner.Length;
                pool.PoolStats.PoolHashRate = poolHashrate;
                pool.PoolStats.ValidSharesPerSecond = (int) (poolHashesCountAccumulated / windowActual);

                // persist
                cf.RunTx((con, tx) =>
                {
                    var mapped = mapper.Map<Persistence.Model.PoolStats>(pool.PoolStats);
                    mapped.PoolId = poolId;
                    mapped.Created = start;

                    statsRepo.InsertPoolStats(con, tx, mapped);
                });

                // calculate & update miner, worker hashrates
                foreach (var minerHashes in byMiner)
                {
                    cf.RunTx((con, tx) =>
                    {
                        stats.Miner = minerHashes.Key;

                        foreach (var item in minerHashes)
                        {
                            // calculate miner/worker stats
                            windowActual = Math.Max(1, (minerHashes.Max(x => x.LastShare) - minerHashes.Min(x => x.FirstShare)).TotalSeconds);
                            var hashrate = pool.HashrateFromShares(item.Sum, windowActual);

                            // update
                            stats.Hashrate = hashrate;
                            stats.Worker = item.Worker;
                            stats.SharesPerSecond = (double) item.Count / HashrateCalculationWindow;

                            // persist
                            statsRepo.InsertMinerWorkerPerformanceStats(con, tx, stats);
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
