using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Autofac;
using AutoMapper;
using Microsoft.Extensions.Hosting;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;
using Miningcore.Util;
using NLog;
using Polly;

namespace Miningcore.Mining;

public class StatsRecorder : BackgroundService
{
    public StatsRecorder(IComponentContext ctx,
        IMasterClock clock,
        IConnectionFactory cf,
        IMessageBus messageBus,
        IMapper mapper,
        ClusterConfig clusterConfig,
        IShareRepository shareRepo,
        IStatsRepository statsRepo)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(cf);
        Contract.RequiresNonNull(messageBus);
        Contract.RequiresNonNull(mapper);
        Contract.RequiresNonNull(shareRepo);
        Contract.RequiresNonNull(statsRepo);

        this.clock = clock;
        this.cf = cf;
        this.mapper = mapper;
        this.messageBus = messageBus;
        this.shareRepo = shareRepo;
        this.statsRepo = statsRepo;
        this.clusterConfig = clusterConfig;

        updateInterval = TimeSpan.FromSeconds(clusterConfig.Statistics?.UpdateInterval ?? 120);
        gcInterval = TimeSpan.FromHours(clusterConfig.Statistics?.GcInterval ?? 4);
        hashrateCalculationWindow = TimeSpan.FromMinutes(clusterConfig.Statistics?.HashrateCalculationWindow ?? 10);
        cleanupDays  = TimeSpan.FromDays(clusterConfig.Statistics?.CleanupDays ?? 180);

        BuildFaultHandlingPolicy();
    }

    private readonly IMasterClock clock;
    private readonly IStatsRepository statsRepo;
    private readonly IConnectionFactory cf;
    private readonly IMapper mapper;
    private readonly IMessageBus messageBus;
    private readonly IShareRepository shareRepo;
    private readonly ClusterConfig clusterConfig;
    private readonly CompositeDisposable disposables = new();
    private readonly ConcurrentDictionary<string, IMiningPool> pools = new();
    private readonly TimeSpan updateInterval;
    private readonly TimeSpan cleanupDays;
    private readonly TimeSpan gcInterval;
    private readonly TimeSpan hashrateCalculationWindow;
    private const int RetryCount = 4;
    private IAsyncPolicy readFaultPolicy;

    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

    private void AttachPool(IMiningPool pool)
    {
        pools.TryAdd(pool.Config.Id, pool);
    }

    private void OnPoolStatusNotification(PoolStatusNotification notification)
    {
        if(notification.Status == PoolStatus.Online)
            AttachPool(notification.Pool);
    }

    private async Task UpdatePoolHashratesAsync(CancellationToken ct)
    {
        var now = clock.Now;
        var timeFrom = now.Add(-hashrateCalculationWindow);

        var stats = new MinerWorkerPerformanceStats
        {
            Created = now
        };

        foreach(var poolId in pools.Keys)
        {
            if(ct.IsCancellationRequested)
                return;

            stats.PoolId = poolId;

            logger.Info(() => $"[{poolId}] Updating Statistics for pool");

            var pool = pools[poolId];

            // fetch stats for window
            var result = await readFaultPolicy.ExecuteAsync(() =>
                cf.Run(con => shareRepo.GetHashAccumulationBetweenAsync(con, poolId, timeFrom, now, ct)));

            var byMiner = result.GroupBy(x => x.Miner).ToArray();

            if (result.Length > 0)
            {
                // pool miners
                pool.PoolStats.ConnectedMiners = byMiner.Length; // update connected miners

                // Stats calc windows
                var timeFrameBeforeFirstShare = ((result.Min(x => x.FirstShare) - timeFrom).TotalSeconds);
                var timeFrameAfterLastShare   = ((now - result.Max(x => x.LastShare)).TotalSeconds);
                var timeFrameFirstLastShare   = (hashrateCalculationWindow.TotalSeconds - timeFrameBeforeFirstShare - timeFrameAfterLastShare);
                //var poolHashTimeFrame         = Math.Floor(TimeFrameFirstLastShare + (TimeFrameBeforeFirstShare / 3) + (TimeFrameAfterLastShare * 3)) ;

                var poolHashTimeFrame = hashrateCalculationWindow.TotalSeconds;

                // pool hashrate
                var poolHashesAccumulated = result.Sum(x => x.Sum);
                var poolHashrate = pool.HashrateFromShares(poolHashesAccumulated, poolHashTimeFrame);
                poolHashrate = Math.Floor(poolHashrate);
                pool.PoolStats.PoolHashrate = (ulong) poolHashrate;

                // pool shares
                var poolHashesCountAccumulated = result.Sum(x => x.Count);
                pool.PoolStats.SharesPerSecond = (int) (poolHashesCountAccumulated / poolHashTimeFrame);

                messageBus.NotifyHashrateUpdated(pool.Config.Id, poolHashrate);
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

            // persist
            await cf.RunTx(async (con, tx) =>
            {
                var mapped = new Persistence.Model.PoolStats
                {
                    PoolId = poolId,
                    Created = now
                };

                mapper.Map(pool.PoolStats, mapped);
                mapper.Map(pool.NetworkStats, mapped);

                await statsRepo.InsertPoolStatsAsync(con, tx, mapped, ct);
            });

            // retrieve most recent miner/worker non-zero hashrate sample
            var previousMinerWorkerHashrates = await cf.Run(con =>
                statsRepo.GetPoolMinerWorkerHashratesAsync(con, poolId, ct));

            const char keySeparator = '.';

            string BuildKey(string miner, string worker = null)
            {
                return !string.IsNullOrEmpty(worker) ? $"{miner}{keySeparator}{worker}" : miner;
            }

            var previousNonZeroMinerWorkers = new HashSet<string>(
                previousMinerWorkerHashrates.Select(x => BuildKey(x.Miner, x.Worker)));

            var currentNonZeroMinerWorkers = new HashSet<string>();

            foreach (var minerHashes in byMiner)
            {
                if(ct.IsCancellationRequested)
                    return;

                double minerTotalHashrate = 0;

                await cf.RunTx(async (con, tx) =>
                {
                    stats.Miner = minerHashes.Key;

                    // book keeping
                    currentNonZeroMinerWorkers.Add(BuildKey(stats.Miner));

                    foreach (var item in minerHashes)
                    {
                        // set default values
                        stats.Hashrate = 0;
                        stats.SharesPerSecond = 0;

                        // miner stats calculation windows
                        var timeFrameBeforeFirstShare = ((minerHashes.Min(x => x.FirstShare) - timeFrom).TotalSeconds);
                        var timeFrameAfterLastShare   = ((now - minerHashes.Max(x => x.LastShare)).TotalSeconds);

                        var minerHashTimeFrame = hashrateCalculationWindow.TotalSeconds;

                        if(timeFrameBeforeFirstShare >= (hashrateCalculationWindow.TotalSeconds * 0.1) )
                            minerHashTimeFrame = Math.Floor(hashrateCalculationWindow.TotalSeconds - timeFrameBeforeFirstShare );

                        if(timeFrameAfterLastShare   >= (hashrateCalculationWindow.TotalSeconds * 0.1) )
                            minerHashTimeFrame = Math.Floor(hashrateCalculationWindow.TotalSeconds + timeFrameAfterLastShare   );

                        if( (timeFrameBeforeFirstShare >= (hashrateCalculationWindow.TotalSeconds * 0.1)) && (timeFrameAfterLastShare >= (hashrateCalculationWindow.TotalSeconds * 0.1)) )
                            minerHashTimeFrame = (hashrateCalculationWindow.TotalSeconds - timeFrameBeforeFirstShare + timeFrameAfterLastShare);

                        if(minerHashTimeFrame < 1)
                            minerHashTimeFrame = 1;

                        // calculate miner/worker stats
                        var minerHashrate = pool.HashrateFromShares(item.Sum, minerHashTimeFrame);
                        minerHashrate = Math.Floor(minerHashrate);
                        minerTotalHashrate += minerHashrate;
                        stats.Hashrate = minerHashrate;
                        stats.Worker = item.Worker;

                        stats.SharesPerSecond = Math.Round(item.Count / minerHashTimeFrame, 3);

                        // persist
                        await statsRepo.InsertMinerWorkerPerformanceStatsAsync(con, tx, stats, ct);

                        // broadcast
                        messageBus.NotifyHashrateUpdated(pool.Config.Id, minerHashrate, stats.Miner, stats.Worker);

                        logger.Info(() => $"[{poolId}] Worker {stats.Miner}{(!string.IsNullOrEmpty(stats.Worker) ? $".{stats.Worker}" : string.Empty)}: {FormatUtil.FormatHashrate(minerHashrate)}, {stats.SharesPerSecond} shares/sec");

                        // book keeping
                        currentNonZeroMinerWorkers.Add(BuildKey(stats.Miner, stats.Worker));
                    }
                });

                messageBus.NotifyHashrateUpdated(pool.Config.Id, minerTotalHashrate, stats.Miner, null);

                logger.Info(() => $"[{poolId}] Miner {stats.Miner}: {FormatUtil.FormatHashrate(minerTotalHashrate)}");
            }

            // identify and reset "orphaned" miner stats
            var orphanedHashrateForMinerWorker = previousNonZeroMinerWorkers.Except(currentNonZeroMinerWorkers).ToArray();

            if(orphanedHashrateForMinerWorker.Any())
            {
                async Task Action(IDbConnection con, IDbTransaction tx)
                {
                    // reset
                    stats.Hashrate = 0;
                    stats.SharesPerSecond = 0;

                    foreach(var item in orphanedHashrateForMinerWorker)
                    {
                        var parts = item.Split(keySeparator);
                        var miner = parts[0];
                        var worker = parts.Length > 1 ? parts[1] : null;

                        stats.Miner = miner;
                        stats.Worker = worker;

                        // persist
                        await statsRepo.InsertMinerWorkerPerformanceStatsAsync(con, tx, stats, ct);

                        // broadcast
                        messageBus.NotifyHashrateUpdated(pool.Config.Id, 0, stats.Miner, stats.Worker);

                        if(string.IsNullOrEmpty(stats.Worker))
                            logger.Info(() => $"[{poolId}] Reset performance stats for miner {stats.Miner}");
                        else
                            logger.Info(() => $"[{poolId}] Reset performance stats for miner {stats.Miner}.{stats.Worker}");
                    }
                }

                await cf.RunTx(Action);
            }
        }
    }

    private async Task StatsGcAsync(CancellationToken ct)
    {
        logger.Info(() => "Performing Stats GC");

        await cf.Run(async con =>
        {
            var cutOff = clock.Now.Add(-cleanupDays);

            var rowCount = await statsRepo.DeletePoolStatsBeforeAsync(con, cutOff, ct);
            if(rowCount > 0)
                logger.Info(() => $"Deleted {rowCount} old poolstats records");

            rowCount = await statsRepo.DeleteMinerStatsBeforeAsync(con, cutOff, ct);
            if(rowCount > 0)
                logger.Info(() => $"Deleted {rowCount} old minerstats records");
        });

        logger.Info(() => "Stats GC complete");
    }

    private async Task UpdateAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(updateInterval);

        do
        {
            try
            {
                await UpdatePoolHashratesAsync(ct);
            }

            catch(OperationCanceledException)
            {
                // ignored
            }

            catch(Exception ex)
            {
                logger.Error(ex);
            }
        } while(await timer.WaitForNextTickAsync(ct));
    }

    private async Task GcAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(gcInterval);

        do
        {
            try
            {
                await StatsGcAsync(ct);
            }

            catch(OperationCanceledException)
            {
                // ignored
            }

            catch(Exception ex)
            {
                logger.Error(ex);
            }
        } while(await timer.WaitForNextTickAsync(ct));
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

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            // monitor pool lifetime
            disposables.Add(messageBus.Listen<PoolStatusNotification>()
                .ObserveOn(TaskPoolScheduler.Default)
                .Subscribe(OnPoolStatusNotification));

            logger.Info(() => "Online");

            // warm-up delay
            await Task.Delay(TimeSpan.FromSeconds(15), ct);

            await Task.WhenAll(
                UpdateAsync(ct),
                GcAsync(ct));

            logger.Info(() => "Offline");
        }

        finally
        {
            disposables.Dispose();
        }
    }
}
