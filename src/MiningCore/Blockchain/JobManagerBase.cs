/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Util;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Blockchain
{
    public abstract class JobManagerBase<TJob>
    {
        protected JobManagerBase(IComponentContext ctx)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));

            this.ctx = ctx;
        }

        protected readonly IComponentContext ctx;
        protected ClusterConfig clusterConfig;

        protected TJob currentJob;
        private int jobId;
        protected object jobLock = new object();
        protected ILogger logger;
        protected PoolConfig poolConfig;
        protected virtual string LogCat { get; } = "Job Manager";
        public IObservable<PoolConfig> ScheduledUpdateJob { get; private set; }
        protected readonly CompositeDisposable disposables = new CompositeDisposable();

        protected abstract void ConfigureDaemons();

        protected virtual async Task StartDaemonAsync()
        {
            while(!await AreDaemonsHealthyAsync())
            {
                logger.Info(() => $"[{LogCat}] Waiting for daemons to come online ...");

                await Task.Delay(TimeSpan.FromSeconds(10));
            }

            logger.Info(() => $"[{LogCat}] All daemons online");

            while(!await AreDaemonsConnectedAsync())
            {
                logger.Info(() => $"[{LogCat}] Waiting for daemons to connect to peers ...");

                await Task.Delay(TimeSpan.FromSeconds(10));
            }

            if (poolConfig.UpdateInterval > 0)
            {
                ScheduledUpdateJob = Observable.Interval(TimeSpan.FromSeconds(poolConfig.UpdateInterval))
                    .DistinctUntilChanged()
                    .Select(_ => { return poolConfig; })
                    .Repeat();
                disposables.Add(ScheduledUpdateJob.Subscribe(RunUpdateJob));
            }
        }

        private void RunUpdateJob(PoolConfig pool)
        {
            try
            {
                RunUpdates(pool);
            } catch (Exception x)
            {
                logger.Error(x, $"[{pool.PoolName}] Exception Running UpdateJob - {x.Message} \n {x.InnerException}");
            }
        }

        protected string NextJobId(string format = null)
        {
            Interlocked.Increment(ref jobId);
            var value = Interlocked.CompareExchange(ref jobId, 0, Int32.MinValue);

            if (format != null)
                return value.ToString(format);

            return value.ToStringHex8();
        }

        protected abstract Task<bool> AreDaemonsHealthyAsync();
        protected abstract Task<bool> AreDaemonsConnectedAsync();
        protected abstract Task EnsureDaemonsSynchedAsync();
        protected abstract Task PostStartInitAsync();

        protected virtual void RunUpdates(PoolConfig config)
        {
            // Override this son of a bitch to periodically do stuff.
        }

        #region API-Surface

        public virtual void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

            logger = LogUtil.GetPoolScopedLogger(typeof(JobManagerBase<TJob>), poolConfig);
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
