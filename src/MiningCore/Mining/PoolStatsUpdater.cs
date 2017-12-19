using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using MiningCore.Configuration;
using MiningCore.Contracts;
using MiningCore.Extensions;
using MiningCore.Payments;
using MiningCore.Persistence;
using MiningCore.Persistence.Repositories;
using MiningCore.Time;
using NLog;
using Polly;

namespace MiningCore.Mining
{
    public class PoolStatsUpdater
    {
        public PoolStatsUpdater(IComponentContext ctx,
            IMasterClock clock,
            IConnectionFactory cf,
            IShareRepository shareRepo,
            IStatsRepository statsRepo)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));

            this.ctx = ctx;
            this.clock = clock;
            this.cf = cf;
            this.shareRepo = shareRepo;
            this.statsRepo = statsRepo;

            BuildFaultHandlingPolicy();
        }

        private readonly IMasterClock clock;
        private readonly IStatsRepository statsRepo;
        private readonly IConnectionFactory cf;
        private readonly IComponentContext ctx;
        private readonly IShareRepository shareRepo;
        private readonly AutoResetEvent stopEvent = new AutoResetEvent(false);
        private readonly Dictionary<string, IMiningPool> pools = new Dictionary<string, IMiningPool>();
        private ClusterConfig clusterConfig;
        private Thread thread;
        private const int RetryCount = 4;
        private Policy shareReadFaultPolicy;

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

                var interval = TimeSpan.FromSeconds(
                    clusterConfig.PaymentProcessing.Interval > 0 ? clusterConfig.PaymentProcessing.Interval : 600);

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

            thread.Name = "Pool Stats Updater";
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
            var target = start.AddMinutes(-10);
            var pageSize = 50000;
            var currentPage = 0;

            foreach (var poolId in pools.Keys)
            {
                logger.Info(() => $"Updating hashrates for pool {poolId}");

                var before = start;
                var pool = pools[poolId];
                var accumulated = 0d;

                while (true)
                {
                    logger.Info(() => $"Fetching page {currentPage} of shares for pool {poolId}");

                    var blockPage = shareReadFaultPolicy.Execute(() =>
                        cf.Run(con => shareRepo.ReadSharesBeforeAndAfterCreated(con, poolId, before, target, true, pageSize)));

                    currentPage++;

                    // accumulate per pool, miner and worker
                    // accumulated += pool.HashrateAccumulate(blockPage);

                    if (blockPage.Length < pageSize)
                        break;

                    before = blockPage[blockPage.Length - 1].Created;
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

            shareReadFaultPolicy = retry;
        }

        private static void OnPolicyRetry(Exception ex, int retry, object context)
        {
            logger.Warn(() => $"Retry {retry} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }
    }
}
