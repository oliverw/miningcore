using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using NLog;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Configuration;
using MiningCore.DaemonInterface;
using MiningCore.Stratum;
using MiningCore.Util;

namespace MiningCore.Blockchain
{
    public abstract class JobManagerBase<TJob>
    {
        protected JobManagerBase(IComponentContext ctx, DaemonClient daemon)
        {
	        Contract.RequiresNonNull(ctx, nameof(ctx));
	        Contract.RequiresNonNull(daemon, nameof(daemon));

			this.ctx = ctx;
            this.daemon = daemon;
        }

        protected readonly IComponentContext ctx;
	    protected PoolConfig poolConfig;
	    protected ClusterConfig clusterConfig;
        protected DaemonClient daemon;
        protected ILogger logger;

	    protected TJob currentJob;
	    protected object jobLock = new object();
	    private long jobId;

	    #region API-Surface

	    public virtual void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
	    {
		    Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
			Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

		    this.logger = LogUtil.GetPoolScopedLogger(typeof(BitcoinJobManager), poolConfig);
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

	    protected virtual string LogCat { get; } = "Job Manager";

		protected abstract Task<bool> IsDaemonHealthy();
	    protected abstract Task<bool> IsDaemonConnected();
		protected abstract Task EnsureDaemonsSynchedAsync();
        protected abstract Task PostStartInitAsync();
    }
}
