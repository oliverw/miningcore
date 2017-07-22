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
using MiningForce.Authorization;
using MiningForce.Configuration;
using MiningForce.Stratum;

namespace MiningForce.Blockchain
{
    public abstract class JobManagerBase<TWorkerContext, TJob>
        where TWorkerContext: class, new()
    {
        protected JobManagerBase(IComponentContext ctx, ILogger logger, 
			PoolConfig poolConfig, ClusterConfig clusterConfig, BlockchainDaemon daemon)
        {
            this.ctx = ctx;
            this.logger = logger;
            this.daemon = daemon;
	        this.poolConfig = poolConfig;
	        this.clusterConfig = clusterConfig;
        }

        protected readonly IComponentContext ctx;
	    protected readonly PoolConfig poolConfig;
	    protected readonly ClusterConfig clusterConfig;
        protected BlockchainDaemon daemon;
        protected StratumServer stratum;
        private IWorkerAuthorizer authorizer;
        protected readonly ILogger logger;
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

        public async Task StartAsync(StratumServer stratum)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(stratum, nameof(stratum));

	        logger.Info(() => $"[{poolConfig.Coin.Type}] Starting ...");

			this.stratum = stratum;
	        this.jobRebroadcastTimeout = TimeSpan.FromSeconds(poolConfig.JobRebroadcastTimeout);

			SetupAuthorizer();
            await StartDaemonAsync();
            await EnsureDaemonsSynchedAsync();
            await PostStartInitAsync();
            SetupJobStream();

            logger.Info(() => $"[{poolConfig.Coin.Type}] Online");
        }

        #endregion // API-Surface

        protected virtual void SetupAuthorizer()
        {
            authorizer = ctx.ResolveNamed<IWorkerAuthorizer>(poolConfig.Authorizer.ToString());
        }

        /// <summary>
        /// Authorizes workers using configured authenticator
        /// </summary>
        /// <param name="worker">StratumClient requesting authorization</param>
        /// <param name="workername">Name of worker requesting authorization</param>
        /// <param name="password">The password</param>
        /// <returns></returns>
        public virtual Task<bool> AuthenticateWorkerAsync(StratumClient worker, string workername, string password)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(workername), $"{nameof(workername)} must not be empty");

            return authorizer.AuthorizeAsync((IBlockchainJobManager) this, worker.RemoteEndpoint, workername, password);
        }
        
        protected virtual async Task StartDaemonAsync()
        {
            daemon.Start(poolConfig);

            while (!await IsDaemonHealthy())
            {
                logger.Info(() => $"[{poolConfig.Coin.Type}] Waiting for daemons to come online ...");

                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            logger.Info(() => $"[{poolConfig.Coin.Type}] All daemons online");
        }

        protected virtual void SetupJobStream()
        {
            Jobs = Observable.Create<object>(observer =>
            {
                var interval = TimeSpan.FromMilliseconds(poolConfig.BlockRefreshInterval);
                var abort = false;

                var task = new Task(async () =>
                {
                    while (!abort)
                    {
                        try
                        {
                            var now = DateTime.UtcNow;

							// force an update of current job if we haven't received a new block for a while
	                        var forceUpdate = lastBlockUpdate.HasValue && (now - lastBlockUpdate) > jobRebroadcastTimeout;

							if(forceUpdate)
								logger.Debug(()=> $"[{poolConfig.Coin.Type}] No new blocks for {jobRebroadcastTimeout.TotalSeconds} seconds - updating transactions & rebroadcasting work");

							if (await UpdateJobs(forceUpdate) || forceUpdate)
							{
								var isNew = !forceUpdate;

								if (isNew)
									logger.Info(() => $"[{poolConfig.Coin.Type}] New block detected");

								lastBlockUpdate = now;

	                            // emit new job params
								var jobParams = GetJobParamsForStratum(isNew);
                                observer.OnNext(jobParams);
                            }

                            Thread.Sleep(interval);
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(() => $"[{poolConfig.Coin.Type}] Error during job polling: {ex.Message}");
                        }
                    }
                }, TaskCreationOptions.LongRunning);

                task.Start();

                return Disposable.Create(()=> abort = true);
            })
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

        /// <summary>
        /// Query coin-daemon for job (block) updates and returns true if a new job (block) was detected
        /// </summary>
        protected abstract Task<bool> IsDaemonHealthy();

        protected abstract Task EnsureDaemonsSynchedAsync();
        protected abstract Task PostStartInitAsync(); 

        /// <summary>
        /// Query coin-daemon for job (block) updates and returns true if a new job (block) was detected
        /// </summary>
        protected abstract Task<bool> UpdateJobs(bool forceUpdate);

	    /// <summary>
	    /// Packages current job parameters for stratum update
	    /// </summary>
	    /// <returns></returns>
	    protected abstract object GetJobParamsForStratum(bool isNew);
    }
}
