using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Configuration;
using MiningCore.DaemonInterface;
using MiningCore.Util;
using NLog;

namespace MiningCore.Blockchain
{
    public abstract class JobManagerBase<TJob>
    {
        protected readonly IComponentContext ctx;
        protected ClusterConfig clusterConfig;

        protected TJob currentJob;
        protected DaemonClient daemon;
        private long jobId;
        protected object jobLock = new object();
        protected ILogger logger;
        protected PoolConfig poolConfig;

        protected JobManagerBase(IComponentContext ctx, DaemonClient daemon)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(daemon, nameof(daemon));

            this.ctx = ctx;
            this.daemon = daemon;
        }

        protected virtual string LogCat { get; } = "Job Manager";

        protected virtual void ConfigureDaemons()
        {
            daemon.Configure(poolConfig.Daemons);
        }

        protected virtual async Task StartDaemonAsync()
        {
            while (!await IsDaemonHealthy())
            {
                logger.Info(() => $"[{LogCat}] Waiting for daemons to come online ...");

                await Task.Delay(TimeSpan.FromSeconds(10));
            }

            logger.Info(() => $"[{LogCat}] All daemons online");

            while (!await IsDaemonConnected())
            {
                logger.Info(() => $"[{LogCat}] Waiting for daemons to connect to peers ...");

                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }

        protected string NextJobId()
        {
            return Interlocked.Increment(ref jobId).ToString(CultureInfo.InvariantCulture);
        }

        protected abstract Task<bool> IsDaemonHealthy();
        protected abstract Task<bool> IsDaemonConnected();
        protected abstract Task EnsureDaemonsSynchedAsync();
        protected abstract Task PostStartInitAsync();

        #region API-Surface

        public virtual void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

            logger = LogUtil.GetPoolScopedLogger(typeof(BitcoinJobManager), poolConfig);
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;

            ConfigureDaemons();
        }

        public async Task StartAsync()
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

            logger.Info(() => $"[{LogCat}] Launching ...");

            await StartDaemonAsync();
            await EnsureDaemonsSynchedAsync();
            await PostStartInitAsync();

            logger.Info(() => $"[{LogCat}] Online");
        }

        #endregion // API-Surface
    }
}
