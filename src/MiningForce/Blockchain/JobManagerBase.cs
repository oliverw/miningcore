using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using NLog;
using MiningForce.Blockchain.Bitcoin;
using MiningForce.Configuration;
using MiningForce.DaemonInterface;
using MiningForce.Stratum;
using MiningForce.Util;

namespace MiningForce.Blockchain
{
    public abstract class JobManagerBase<TWorkerContext, TJob>
        where TWorkerContext: class, new()
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
        protected StratumServer stratum;
        protected ILogger logger;
	    private TimeSpan jobRebroadcastTimeout;
	    protected DateTime? lastBlockUpdate;

		protected readonly ConditionalWeakTable<StratumClient, TWorkerContext> workerContexts =
            new ConditionalWeakTable<StratumClient, TWorkerContext>();

		protected readonly Dictionary<string, TJob> validJobs = new Dictionary<string, TJob>();
	    protected TJob currentJob;
	    protected object jobLock = new object();
	    private long jobId;

	    #region API-Surface

		public IObservable<object> Jobs { get; private set; }

	    public virtual void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
	    {
		    Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
			Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

		    this.logger = LogUtil.GetPoolScopedLogger(typeof(BitcoinJobManager), poolConfig);
			this.poolConfig = poolConfig;
		    this.clusterConfig = clusterConfig;

		    ConfigureDaemons();
	    }

	    public async Task StartAsync(StratumServer stratum)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(stratum, nameof(stratum));

	        logger.Info(() => $"[{LogCategory}] Launching ...");

			this.stratum = stratum;
	        this.jobRebroadcastTimeout = TimeSpan.FromSeconds(poolConfig.JobRebroadcastTimeout);

            await StartDaemonAsync();
            await EnsureDaemonsSynchedAsync();
            await PostStartInitAsync();
            CreateJobStream();

            logger.Info(() => $"[{LogCategory}] Online");
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
                logger.Info(() => $"[{LogCategory}] Waiting for daemons to come online ...");

                await Task.Delay(TimeSpan.FromSeconds(10));
            }

            logger.Info(() => $"[{LogCategory}] All daemons online");

	        while (!await IsDaemonConnected())
	        {
		        logger.Info(() => $"[{LogCategory}] Waiting for daemons to connect to peers ...");

		        await Task.Delay(TimeSpan.FromSeconds(10));
	        }
		}

		protected virtual void CreateJobStream()
		{
			// periodically update job from daemon
			var newJobs = Observable.Interval(TimeSpan.FromMilliseconds(poolConfig.BlockRefreshInterval))
				.Select(_ => Observable.FromAsync(() => UpdateJob(false)))
				.Concat()
				.Do(isNew =>
				{
					if (isNew)
						logger.Info(() => $"[{LogCategory}] New block detected");
				})
				.Where(isNew => isNew)
				.Publish()
				.RefCount();

			// if there haven't been any new jobs for a while, force an update
			var forcedNewJobs = Observable.Timer(jobRebroadcastTimeout)
				.TakeUntil(newJobs)		// cancel timeout if an actual new job has been detected
				.Do(_=> logger.Info(() => $"[{LogCategory}] No new blocks for {jobRebroadcastTimeout.TotalSeconds} seconds - " +
				                           $"updating transactions & rebroadcasting work"))
				.Select(x => Observable.FromAsync(() => UpdateJob(true)))
				.Concat()
				.Repeat();

			Jobs = Observable.Merge(newJobs, forcedNewJobs)
				.Select(GetJobParamsForStratum)
				.Publish()
				.RefCount();
        }

        protected TWorkerContext GetWorkerContext(StratumClient client)
        {
            TWorkerContext context;

            lock (workerContexts)
            {
                if (!workerContexts.TryGetValue(client, out context))
                {
                    context = new TWorkerContext();
                    workerContexts.Add(client, context);
                }
            }

            return context;
        }

	    protected string NextJobId()
	    {
		    return Interlocked.Increment(ref jobId).ToString("x", CultureInfo.InvariantCulture);
	    }

	    protected virtual string LogCategory { get; } = "Job Manager";

		protected abstract Task<bool> IsDaemonHealthy();
	    protected abstract Task<bool> IsDaemonConnected();

		protected abstract Task EnsureDaemonsSynchedAsync();
        protected abstract Task PostStartInitAsync();

		/// <summary>
		/// Refresh network stats
		/// </summary>
	    protected abstract Task UpdateNetworkStats();

		/// <summary>
		/// Query coin-daemon for job (block) updates and returns true if a new job (block) was detected
		/// </summary>
		protected abstract Task<bool> UpdateJob(bool forceUpdate);

	    /// <summary>
	    /// Packages current job parameters for stratum update
	    /// </summary>
	    /// <returns></returns>
	    protected abstract object GetJobParamsForStratum(bool isNew);
    }
}
