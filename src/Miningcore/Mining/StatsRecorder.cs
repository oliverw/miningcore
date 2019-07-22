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

        // MinerNL Stats calculation variables
        private readonly AutoResetEvent stopEvent = new AutoResetEvent(false);
		private const int statsInterval = 60;             // seconds. Default setting if not in config.json
        private const int hashrateCalculationWindow = 10; // minutes. Default setting if not in config.json
        private const int statsCleanupInterval = 96;      // hours.   Default setting if not in config.json
        private const int statsDBCleanupHistory = 180;    // days.    Default setting if not in config.json
        private int _StatsInterval;
        private int _HashrateCalculationWindow;
        private int _StatsCleanupInterval;
        // MinerNL end

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
                logger.Info(() => "Pool Stats Online");

                // warm-up delay
				await Task.Delay(TimeSpan.FromSeconds(10));

                // MinerNL read variables from config.json
                // Stats broadcast interval
                _StatsInterval = clusterConfig.Statistics?.StatsInterval ?? statsInterval;
                if(_StatsInterval == 0)
                {
                    _StatsInterval = statsInterval;
                    logger.Info(() => $"statistics -> statsInterval not found in config.json. using default : {_StatsInterval} seconds");
                }

                // Stats calculation window
                _HashrateCalculationWindow = clusterConfig.Statistics?.HashrateCalculationWindow ?? hashrateCalculationWindow;
                if(_HashrateCalculationWindow == 0)
                {
                    _HashrateCalculationWindow = hashrateCalculationWindow;
                    logger.Info(() => $"statistics -> hashrateCalculationWindow not found in config.json. using default : {_HashrateCalculationWindow} minutes");
                }

                // Stats DB cleanup interval
                _StatsCleanupInterval = clusterConfig.Statistics?.StatsCleanupInterval ?? statsCleanupInterval;
                if(_StatsCleanupInterval == 0)
                {
                    _StatsCleanupInterval = statsCleanupInterval;
                    logger.Info(() => $"statistics -> statsCleanupInterval not found in config.json. using default : {_StatsCleanupInterval} minutes");
                }
                
                // Set DB Cleanup time
                var performStatsGcInterval = DateTime.UtcNow;
				// MinerNL end

                while(!cts.IsCancellationRequested)
                {
                    try
                    {
                        await UpdatePoolHashratesAsync();    // Pool stats update

                        // MinerNL - Stats cleanup at StatsCleanupInterval
                        logger.Info(() => $"Next Stats DB cleanup at {performStatsGcInterval.ToLocalTime()}");
                        if (clock.UtcNow >= performStatsGcInterval)
                        {
                            await PerformStatsGcAsync();
                            performStatsGcInterval = DateTime.UtcNow.AddHours(_StatsCleanupInterval);
                        }
						// MinerNL end
                    }

                    catch(Exception ex)
                    {
                        logger.Error(ex);
                    }

					await Task.Delay(TimeSpan.FromSeconds(_StatsInterval), cts.Token);
                    
                }
            });
        }
        public void Stop()
        {
            logger.Info(() => "Pool Stopping ..");

            cts.Cancel();

            logger.Info(() => "Pool Stopped");
        }

        #endregion // API-Surface

        private async Task UpdatePoolHashratesAsync()
        {

            DateTime CurrentTimeUtc = clock.UtcNow;
            var TimeFrom = CurrentTimeUtc.AddMinutes(-_HashrateCalculationWindow);
            var StatsWindowsTimeFrame = TimeSpan.FromMinutes(_HashrateCalculationWindow);

            logger.Info(() => "--------------------------------------------------------------------------------------------");
            logger.Info(() => $"CurrentTimeUtc        : {CurrentTimeUtc}");
            logger.Info(() => $"TimeFrom              : {TimeFrom}");
            logger.Info(() => $"StatsWindowsTimeFrame : {StatsWindowsTimeFrame}");
            logger.Info(() => "--------------------------------------------------------------------------------------------");
            // MinerNL

            var stats = new MinerWorkerPerformanceStats
            {
                Created = CurrentTimeUtc      // MinerNL Time to UTC
            };

            foreach(var poolId in pools.Keys)
            {
                stats.PoolId = poolId;

                logger.Info(() => $"[{poolId}] Updating Statistics for pool");

                var pool = pools[poolId];
				
				// fetch stats from DB for the last X minutes
                // MinerNL get stats
                var result = await readFaultPolicy.ExecuteAsync(() =>
                    cf.Run(con => shareRepo.GetHashAccumulationBetweenCreatedAsync(con, poolId, TimeFrom, CurrentTimeUtc)));

                var byMiner = result.GroupBy(x => x.Miner).ToArray();

                // calculate & update pool, connected workers & hashrates
                if (result.Length > 0)
                {
                    // pool miners 
                    pool.PoolStats.ConnectedMiners = byMiner.Length; // update connected miners

                    // Stats calc windows
                    var TimeFrameBeforeFirstShare = ((result.Min(x => x.FirstShare) - TimeFrom).TotalSeconds);
                    var TimeFrameAfterLastShare   = ((CurrentTimeUtc - result.Max(x => x.LastShare)).TotalSeconds);
                    var TimeFrameFirstLastShare   = (StatsWindowsTimeFrame.TotalSeconds - TimeFrameBeforeFirstShare - TimeFrameAfterLastShare);

                    //var poolHashTimeFrame         = Math.Floor(TimeFrameFirstLastShare + (TimeFrameBeforeFirstShare / 3) + (TimeFrameAfterLastShare * 3)) ;

                    var poolHashTimeFrame = StatsWindowsTimeFrame.TotalSeconds;

                    // pool hashrate
                    var poolHashesAccumulated = result.Sum(x => x.Sum);
                    var poolHashrate = pool.HashrateFromShares(poolHashesAccumulated, poolHashTimeFrame);
                    poolHashrate = Math.Floor(poolHashrate);
                    pool.PoolStats.PoolHashrate = poolHashrate;

                    // pool shares
                    var poolHashesCountAccumulated = result.Sum(x => x.Count);
                    pool.PoolStats.SharesPerSecond = (int) (poolHashesCountAccumulated / poolHashTimeFrame);
						
					messageBus.NotifyHashrateUpdated(pool.Config.Id, poolHashrate);
					// MinerNL end
                }
                else
                {
                    // reset
                    pool.PoolStats.ConnectedMiners = 0;
                    pool.PoolStats.PoolHashrate = 0;
                    pool.PoolStats.SharesPerSecond = 0;

                    messageBus.NotifyHashrateUpdated(pool.Config.Id, 0);

                    logger.Info(() => $"[{poolId}] Reset performance stats for pool");
                }
				logger.Info(() => $"[{poolId}] Connected Miners : {pool.PoolStats.ConnectedMiners} miners");
				logger.Info(() => $"[{poolId}] Pool hashrate    : {pool.PoolStats.PoolHashrate} hashes/sec");
                logger.Info(() => $"[{poolId}] Pool shares      : {pool.PoolStats.SharesPerSecond} shares/sec");
                
				// persist. Save pool stats in DB.
                await cf.RunTx(async (con, tx) =>
                {
                    var mapped = new Persistence.Model.PoolStats
                    {
                        PoolId = poolId,
                        Created = CurrentTimeUtc   // MinerNL time to UTC
                    };

                    mapper.Map(pool.PoolStats, mapped);
                    mapper.Map(pool.NetworkStats, mapped);

                    await statsRepo.InsertPoolStatsAsync(con, tx, mapped);
                });

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

                if(result.Length == 0)
                {
                    // identify and reset "orphaned" miner stats
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
                                logger.Info(() => $"[{poolId}] Reset performance stats for miner {stats.Miner}");
                            else
                                logger.Info(() => $"[{poolId}] Reset performance stats for miner {stats.Miner}.{stats.Worker}");
                        }
                    });
                    logger.Info(() => "--------------------------------------------");
                    continue;
                };

				// MinerNL calculate & update miner, worker hashrates
                foreach (var minerHashes in byMiner)
				{
                    double minerTotalHashrate = 0;

                    await cf.RunTx(async (con, tx) =>
                    {
                        stats.Miner = minerHashes.Key;
								
						// book keeping
						currentNonZeroMinerWorkers.Add(buildKey(stats.Miner));
								
                        foreach (var item in minerHashes)
                        {
                            // set default values
                            double minerHashrate = 0;
                            stats.Worker = "Default_Miner";
                            stats.Hashrate = 0;
                            stats.SharesPerSecond = 0;

                            // miner stats calculation windows
                            var TimeFrameBeforeFirstShare = ((minerHashes.Min(x => x.FirstShare) - TimeFrom).TotalSeconds);
                            var TimeFrameAfterLastShare   = ((CurrentTimeUtc - minerHashes.Max(x => x.LastShare)).TotalSeconds);
                            var TimeFrameFirstLastShare   = (StatsWindowsTimeFrame.TotalSeconds - TimeFrameBeforeFirstShare - TimeFrameAfterLastShare);

                            //var minerHashTimeFrame        = Math.Floor(TimeFrameFirstLastShare + (TimeFrameBeforeFirstShare / 2) + (TimeFrameAfterLastShare * 2)) ;

                            var minerHashTimeFrame = StatsWindowsTimeFrame.TotalSeconds;

                            if(TimeFrameBeforeFirstShare >= (StatsWindowsTimeFrame.TotalSeconds * 0.1) )
                                minerHashTimeFrame = Math.Floor(StatsWindowsTimeFrame.TotalSeconds - TimeFrameBeforeFirstShare );
                           
                            if(TimeFrameAfterLastShare   >= (StatsWindowsTimeFrame.TotalSeconds * 0.1) )
                                minerHashTimeFrame = Math.Floor(StatsWindowsTimeFrame.TotalSeconds + TimeFrameAfterLastShare   );

                            if( (TimeFrameBeforeFirstShare >= (StatsWindowsTimeFrame.TotalSeconds * 0.1)) && (TimeFrameAfterLastShare >= (StatsWindowsTimeFrame.TotalSeconds * 0.1)) )
                                minerHashTimeFrame = (StatsWindowsTimeFrame.TotalSeconds - TimeFrameBeforeFirstShare + TimeFrameAfterLastShare);
                           

                            logger.Info(() => $"[{poolId}] StatsWindowsTimeFrame : {StatsWindowsTimeFrame.TotalSeconds} | minerHashTimeFrame : {minerHashTimeFrame} |  TimeFrameFirstLastShare : {TimeFrameFirstLastShare} | TimeFrameBeforeFirstShare: {TimeFrameBeforeFirstShare} | TimeFrameAfterLastShare: {TimeFrameAfterLastShare}");

                            // calculate miner/worker stats
                            minerHashrate = pool.HashrateFromShares(item.Sum, minerHashTimeFrame);
                            minerHashrate = Math.Floor(minerHashrate);
                            minerTotalHashrate += minerHashrate;
                            stats.Hashrate = minerHashrate;

                            if (item.Worker != null) {stats.Worker = item.Worker;}
                            stats.SharesPerSecond = Math.Round(((double) item.Count / minerHashTimeFrame),3);

                            // persist. Save miner stats in DB.
                            await statsRepo.InsertMinerWorkerPerformanceStatsAsync(con, tx, stats);

                            // broadcast
							messageBus.NotifyHashrateUpdated(pool.Config.Id, minerHashrate, stats.Miner, stats.Worker);
							logger.Info(() => $"[{poolId}] Miner: {stats.Miner}.{stats.Worker} | Hashrate: {minerHashrate} | HashTimeFrame : {minerHashTimeFrame} | Shares per sec: {stats.SharesPerSecond}");
                                    
							// book keeping
							currentNonZeroMinerWorkers.Add(buildKey(stats.Miner, stats.Worker));
									
						}
                    });
							
                    messageBus.NotifyHashrateUpdated(pool.Config.Id, minerTotalHashrate, stats.Miner, null);
					logger.Info(() => $"[{poolId}] Total miner hashrate: {stats.Miner} | {minerTotalHashrate}");
                }
				// MinerNL end calculate & update miner, worker hashrates

                logger.Info(() => "--------------------------------------------");
            }
        }

        private async Task PerformStatsGcAsync()
        {
            logger.Info(() => $"Performing stats DB cleanup");

            await cf.Run(async con =>
            {
				// MinerNL Stats cleanup
                var _StatsDBCleanupHistory = clusterConfig.Statistics?.StatsDBCleanupHistory ?? statsDBCleanupHistory;
                if(_StatsDBCleanupHistory == 0)
                {
                    _StatsDBCleanupHistory = statsDBCleanupHistory;
                    logger.Info(() => $"statistics -> statsDBCleanupHistory not found in config.json. using default : {statsDBCleanupHistory} days");
                }

                logger.Info(() => $"Removing all stats older then {_StatsDBCleanupHistory} days");

                var cutOff = DateTime.UtcNow.AddDays(-_StatsDBCleanupHistory);
                // MinerNL end
				
                var rowCount = await statsRepo.DeletePoolStatsBeforeAsync(con, cutOff);
                if(rowCount > 0)
                    logger.Info(() => $"Deleted {rowCount} old poolstats records");

                rowCount = await statsRepo.DeleteMinerStatsBeforeAsync(con, cutOff);
                if(rowCount > 0)
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
