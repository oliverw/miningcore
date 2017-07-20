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
using Microsoft.Extensions.Logging;
using MiningForce.Authorization;
using MiningForce.Configuration;
using MiningForce.Configuration.Extensions;
using MiningForce.MininigPool;
using MiningForce.Stratum;

namespace MiningForce.Blockchain
{
    public abstract class JobManagerBase<TWorkerContext, TJob>
        where TWorkerContext: class, new()
    {
        protected JobManagerBase(IComponentContext ctx, ILogger logger, BlockchainDaemon daemon, PoolConfig poolConfig)
        {
            this.ctx = ctx;
            this.logger = logger;
            this.daemon = daemon;
	        this.poolConfig = poolConfig;
        }

        protected readonly IComponentContext ctx;
        protected BlockchainDaemon daemon;
        protected StratumServer stratum;
        private IWorkerAuthorizer authorizer;
        protected readonly PoolConfig poolConfig;
        protected readonly ILogger logger;
	    private TimeSpan jobRebroadcastTimeout;
	    protected DateTime? lastBlockUpdate;

		protected readonly ConditionalWeakTable<StratumClient, TWorkerContext> workerContexts =
            new ConditionalWeakTable<StratumClient, TWorkerContext>();

		protected readonly Dictionary<string, TJob> validJobs = new Dictionary<string, TJob>();
	    protected TJob currentJob;
	    protected object jobLock = new object();
	    private long jobId = 0;

		#region API-Surface

		public IObservable<object> Jobs { get; private set; }

        public async Task StartAsync(StratumServer stratum)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(stratum, nameof(stratum));

            this.stratum = stratum;
	        this.jobRebroadcastTimeout = TimeSpan.FromSeconds(poolConfig.JobRebroadcastTimeout);

			SetupAuthorizer();
            await StartDaemonAsync();
            await EnsureDaemonsSynchedAsync();
            await PostStartInitAsync();
            SetupJobPolling();

            logger.Info(() => $"[{poolConfig.Coin.Type}] Manager started");
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
        public virtual Task<bool> HandleWorkerAuthenticateAsync(StratumClient worker, string workername, string password)
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

            logger.Info(() => $"[{poolConfig.Coin.Type}] All coin-daemons are online");
        }

        protected virtual void SetupJobPolling()
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
                            logger.Warning(() => $"[{poolConfig.Coin.Type}] Error during job polling: {ex.Message}");
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
