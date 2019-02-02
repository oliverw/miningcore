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
using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

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
        private const int StatsInterval = 5; // minutes
        private const int StatsCleanupInterval = 24; // hours
        private const int StatsDBCleanupHistory = 90;  // days
        private const int HashrateCalculationWindow = 60; // minutes
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

                var _StatsInterval = clusterConfig.Statistics?.StatsInterval ?? StatsInterval;
                var poolStatsInterval = TimeSpan.FromMinutes(_StatsInterval);
                var performStatsGcInterval = DateTime.UtcNow;

                while (true)
                {
                    try
                    {
                        await UpdatePoolHashratesAsync();    // Pool stats update

                        // Stats cleanup at interval
                        logger.Info(() => $"Next Stats DB cleanup at {performStatsGcInterval.ToLocalTime()}");
                        if (clock.UtcNow >= performStatsGcInterval)
                        {
                            await PerformStatsGcAsync();
                            var _StatsCleanupInterval = clusterConfig.Statistics?.StatsCleanupInterval ?? StatsCleanupInterval;
                            performStatsGcInterval = DateTime.UtcNow.AddHours(_StatsCleanupInterval);
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
            var _HashrateCalculationWindow = clusterConfig.Statistics?.HashrateCalculationWindow ?? HashrateCalculationWindow;
            var CurrentTimeUtc = clock.UtcNow;
            var TimeFrom = CurrentTimeUtc.AddMinutes(-_HashrateCalculationWindow);
            var StatsWindowsTimeFrame = TimeSpan.FromMinutes(_HashrateCalculationWindow);
   
            var stats = new MinerWorkerPerformanceStats
            {
                Created = CurrentTimeUtc
            };

            foreach(var poolId in pools.Keys)
            {
                stats.PoolId = poolId;

                logger.Info(() => $"Updating hashrates for pool {poolId}");

                var pool = pools[poolId];
                pool.PoolStats.ConnectedMiners = 0;
                pool.PoolStats.PoolHashrate = 0;
                pool.PoolStats.SharesPerSecond = 0;
                double poolHashrate = 0;

                // fetch stats from DB for the last X minutes
                var result = await readFaultPolicy.ExecuteAsync(() =>
                    cf.Run(con => shareRepo.GetHashAccumulationBetweenCreatedAsync(con, poolId, TimeFrom.AddMinutes(-(StatsInterval*2)), CurrentTimeUtc)));

                var byMiner = result.GroupBy(x => x.Miner).ToArray();

                // calculate & update pool, connected workers & hashrates
                if (result.Length > 0)
                {
                    if (result.Max(x => x.LastShare) >= TimeFrom)
                    {
                        pool.PoolStats.ConnectedMiners = byMiner.Length; // update connected miners

                        // Stats calc windows
                        var TimeFrameBeforeFirstShare = ((result.Min(x => x.FirstShare) - TimeFrom).TotalSeconds)/2;
                        var TimeFrameAfterLastShare = ((CurrentTimeUtc - result.Max(x => x.LastShare)).TotalSeconds);
                        var poolHashTimeFrame = StatsWindowsTimeFrame.TotalSeconds - TimeFrameBeforeFirstShare + TimeFrameAfterLastShare;

                        // pool hashrate
                        var poolHashesAccumulated = result.Sum(x => x.Sum);
                        poolHashrate = pool.HashrateFromShares(poolHashesAccumulated, poolHashTimeFrame);
                        poolHashrate = Math.Ceiling(poolHashrate);
                        pool.PoolStats.PoolHashrate = poolHashrate;

                        // pool shares
                        var poolHashesCountAccumulated = 0;  // ToDo: beter calc
                        pool.PoolStats.SharesPerSecond = (int) (poolHashesCountAccumulated / poolHashTimeFrame);
                    }
                    else
                    {
                        poolHashrate = 0;
                    }

                }
                messageBus.NotifyHashrateUpdated(pool.Config.Id, poolHashrate);

                logger.Info(() => $"Connected Miners {poolId}: {pool.PoolStats.ConnectedMiners} miners");
                logger.Info(() => $"Pool hashrate {poolId}: {pool.PoolStats.PoolHashrate} hashes/sec");
                logger.Info(() => $"Pool shares {poolId}: {pool.PoolStats.SharesPerSecond} shares/sec");

                // persist. Save pool stats in DB.
                await cf.RunTx(async (con, tx) =>
                {
                    var mapped = new Persistence.Model.PoolStats
                    {
                        PoolId = poolId,
                        Created = CurrentTimeUtc
                    };

                    mapper.Map(pool.PoolStats, mapped);
                    mapper.Map(pool.NetworkStats, mapped);    // ToDo: Get Network hashrate

                    await statsRepo.InsertPoolStatsAsync(con, tx, mapped);
                });

                if (result.Length == 0)
                {
                    continue;
                }

                if (result.Length > 0)
                {
                    if (result.Max(x => x.LastShare) >= TimeFrom)
                    {
                        // calculate & update miner, worker hashrates
                        foreach (var minerHashes in byMiner)
                        {
                            double minerTotalHashrate = 0;

                            await cf.RunTx(async (con, tx) =>
                            {
                                stats.Miner = minerHashes.Key;
                                foreach (var item in minerHashes)
                                {
                                    double minerHashrate = 0;
                                    stats.Worker = "Default_Miner";
                                    stats.Hashrate = 0;
                                    stats.SharesPerSecond = 0;

                                    var TimeFrameBeforeFirstShare = ((minerHashes.Min(x => x.FirstShare) - TimeFrom).TotalSeconds)/2;
                                    var TimeFrameAfterLastShare = ((CurrentTimeUtc - minerHashes.Max(x => x.LastShare)).TotalSeconds);
                                    var minerHashTimeFrame = StatsWindowsTimeFrame.TotalSeconds - TimeFrameBeforeFirstShare + TimeFrameAfterLastShare;

                                    // calculate miner/worker stats
                                    minerHashrate = pool.HashrateFromShares(item.Sum, minerHashTimeFrame);
                                    minerHashrate = Math.Ceiling(minerHashrate);
                                    minerTotalHashrate += minerHashrate;
                                    stats.Hashrate = minerHashrate;
                                    if (item.Worker != null) {stats.Worker = item.Worker;}
                                    stats.SharesPerSecond = (double) item.Count / minerHashTimeFrame;

                                    // persist. Save miner stats in DB.
                                    await statsRepo.InsertMinerWorkerPerformanceStatsAsync(con, tx, stats);

                                    logger.Info(() => $"Miner: {stats.Miner}.{stats.Worker} | Hashrate: {minerHashrate} | Shares per sec: {stats.SharesPerSecond}");
                                    messageBus.NotifyHashrateUpdated(pool.Config.Id, minerHashrate, stats.Miner, stats.Worker);
                                }
                            });

                            logger.Info(() => $"Total miner hashrate: {stats.Miner} | {minerTotalHashrate}");
                            messageBus.NotifyHashrateUpdated(pool.Config.Id, minerTotalHashrate, stats.Miner, null);
                        }
                    }
                    else
                    {
                        foreach (var minerHashes in byMiner)
                        {
                            await cf.RunTx(async (con, tx) =>
                            {
                                stats.Miner = minerHashes.Key;
                                stats.Worker = "Default_Miner";
                                stats.Hashrate = 0;
                                stats.SharesPerSecond = 0;
                                
                                logger.Info(() => $"Miner: {stats.Miner}.{stats.Worker} | Hashrate: {stats.Hashrate} | Shares per sec: {stats.SharesPerSecond}");
                                messageBus.NotifyHashrateUpdated(pool.Config.Id, stats.Hashrate, stats.Miner, stats.Worker);
                            });
                        }
                    }
                }
            }
        }

        private async Task PerformStatsGcAsync()
        {
            logger.Info(() => $"Performing stats DB cleanup");

            await cf.Run(async con =>
            {
                var _StatsInterval = clusterConfig.Statistics?.StatsInterval ?? StatsInterval;
                var _StatsDBCleanupHistory = clusterConfig.Statistics?.StatsDBCleanupHistory ?? StatsDBCleanupHistory;
                logger.Info(() => $"Removing all stats older then {_StatsDBCleanupHistory} days");

                var cutOff = DateTime.UtcNow.AddDays(-_StatsDBCleanupHistory);
                
                var rowCount = await statsRepo.DeletePoolStatsBeforeAsync(con, cutOff);
                if(rowCount > 0)
                    logger.Info(() => $"Deleted {rowCount} old poolstats records");

                rowCount = await statsRepo.DeleteMinerStatsBeforeAsync(con, cutOff);
                if (rowCount > 0)
                    logger.Info(() => $"Deleted {rowCount} old minerstats records");
            });

            logger.Info(() => $"Stats cleanup DB complete");
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
