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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Autofac;
using Autofac.Features.Metadata;
using AutoMapper;
using MiningCore.Banning;
using MiningCore.Blockchain;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using MiningCore.Stratum;
using MiningCore.Util;
using MiningCore.VarDiff;
using Newtonsoft.Json;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Mining
{
    public abstract class PoolBase<TWorkerContext> : StratumServer<TWorkerContext>,
        IMiningPool
        where TWorkerContext : WorkerContextBase, new()
    {
        protected PoolBase(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IEnumerable<Meta<INotificationSender, NotificationSenderMetadataAttribute>> notificationSenders) : base(ctx)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(notificationSenders, nameof(notificationSenders));

            this.serializerSettings = serializerSettings;
            this.cf = cf;
            this.statsRepo = statsRepo;
            this.mapper = mapper;
            this.notificationSenders = notificationSenders;

            Shares = shareSubject
                .AsObservable()
                .Synchronize();

            validShares = validSharesSubject
                .AsObservable()
                .Synchronize();

            invalidShares = invalidSharesSubject
                .AsObservable()
                .Synchronize();
        }

        protected readonly IObservable<Unit> invalidShares;
        protected readonly Subject<Unit> invalidSharesSubject = new Subject<Unit>();
        protected readonly PoolStats poolStats = new PoolStats();
        protected readonly JsonSerializerSettings serializerSettings;
        private readonly IEnumerable<Meta<INotificationSender, NotificationSenderMetadataAttribute>> notificationSenders;
        protected readonly IConnectionFactory cf;
        protected readonly IStatsRepository statsRepo;
        private readonly IMapper mapper;
        protected readonly Subject<IShare> shareSubject = new Subject<IShare>();
        protected readonly IObservable<IShare> validShares;
        protected readonly CompositeDisposable disposables = new CompositeDisposable();
        protected BlockchainStats blockchainStats;
        protected ClusterConfig clusterConfig;
        protected PoolConfig poolConfig;
        protected readonly Subject<IShare> validSharesSubject = new Subject<IShare>();
        protected readonly Dictionary<PoolEndpoint, VarDiffManager> varDiffManagers =
            new Dictionary<PoolEndpoint, VarDiffManager>();

        protected override string LogCat => "Pool";

        protected abstract Task SetupJobManager();

        protected override void OnConnect(StratumClient<TWorkerContext> client)
        {
            // update stats
            lock (clients)
            {
                poolStats.ConnectedMiners = clients.Count;
            }

            // client setup
            var context = new TWorkerContext();

            var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];
            context.Init(poolConfig, poolEndpoint.Difficulty, poolEndpoint.VarDiff);
            client.Context = context;

            // expect miner to establish communication within a certain time
            EnsureNoZombieClient(client);
        }

        protected override void OnDisconnect(string subscriptionId)
        {
            // update stats
            lock (clients)
            {
                poolStats.ConnectedMiners = clients.Count;
            }
        }

        private void EnsureNoZombieClient(StratumClient<TWorkerContext> client)
        {
            var isAlive = client.Requests
                .Take(1)
                .Select(_ => true);

            var timeout = Observable.Timer(DateTime.UtcNow.AddSeconds(10))
                .Select(_ => false);

            isAlive.Merge(timeout)
                .Take(1)
                .Subscribe(alive =>
                {
                    if (!alive)
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (post-connect silence)");

                        DisconnectClient(client);
                    }
                });
        }

        #region VarDiff

        protected void UpdateVarDiff(StratumClient<TWorkerContext> client, double networkDifficulty)
        {
            var context = client.Context;

            if (context.VarDiff != null)
            {
                logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Updating VarDiff");

                // get or create manager
                VarDiffManager varDiffManager;

                lock (varDiffManagers)
                {
                    var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                    if (!varDiffManagers.TryGetValue(poolEndpoint, out varDiffManager))
                    {
                        varDiffManager = new VarDiffManager(poolEndpoint.VarDiff);
                        varDiffManagers[poolEndpoint] = varDiffManager;
                    }
                }

                // update it
                var newDiff = varDiffManager.Update(context.VarDiff, context.Difficulty, networkDifficulty);
                if (newDiff != null)
                {
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] VarDiff update to {newDiff}");

                    context.EnqueueNewDifficulty(newDiff.Value);
                }
            }
        }

        private void SetupVarDiff()
        {
            if (poolConfig.Ports.Values.Any(x => x.VarDiff != null))
            {
                // Periodically update vardiff
                var interval = poolConfig.Ports.Values
                    .Where(x=> x.VarDiff != null)
                    .Min(x => x.VarDiff.RetargetTime);

                disposables.Add(Observable.Interval(TimeSpan.FromSeconds(interval))
                    .ObserveOn(TaskPoolScheduler.Default)
                    .Do(_ => UpdateVarDiffs())
                    .Subscribe());
            }
        }

        private void UpdateVarDiffs()
        {
            ForEachClient(client =>
            {
                if (client.Context.IsSubscribed)
                {
                    UpdateVarDiff(client);
                }
            });
        }

        protected abstract void UpdateVarDiff(StratumClient<TWorkerContext> client);

        #endregion // VarDiff

        protected void SetupBanning(ClusterConfig clusterConfig)
        {
            if (poolConfig.Banning?.Enabled == true)
            {
                var managerType = clusterConfig.Banning?.Manager ?? BanManagerKind.Integrated;
                banManager = ctx.ResolveKeyed<IBanManager>(managerType);
            }
        }

        protected virtual void SetupStats()
        {
            poolStats.PoolFeePercent = (float) poolConfig.RewardRecipients
                .Where(x => x.Type == RewardRecipientType.Op)
                .Sum(x => x.Percentage);

            poolStats.DonationsPercent = (float) poolConfig.RewardRecipients
                .Where(x => x.Type == RewardRecipientType.Dev)
                .Sum(x => x.Percentage);

            // Periodically persist pool- and blockchain-stats to persistent storage
            disposables.Add(Observable.Interval(TimeSpan.FromSeconds(60))
                .StartWith(0) // initial update
                .Do(_ => UpdateBlockChainStats())
                .Subscribe(_ => PersistStats()));
        }

        protected abstract void UpdateBlockChainStats();

        private void PersistStats()
        {
            try
            {
                logger.Debug(() => $"[{LogCat}] Persisting pool stats");

                cf.RunTx((con, tx) =>
                {
                    var mapped = mapper.Map<Persistence.Model.PoolStats>(poolStats);
                    mapped.PoolId = poolConfig.Id;
                    mapped.Created = DateTime.UtcNow;

                    statsRepo.Insert(con, tx, mapped);
                });
            }

            catch (Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Unable to persist pool stats");
            }
        }
        private void SetupTelemetry()
        {
            // Shares per second
            disposables.Add(validShares.Select(_ => Unit.Default).Merge(invalidShares)
                .Buffer(TimeSpan.FromSeconds(1))
                .Select(shares => shares.Count)
                .Subscribe(count => poolStats.SharesPerSecond = count));

            // Valid/Invalid shares per minute
            disposables.Add(validShares
                .Buffer(TimeSpan.FromMinutes(1))
                .Select(shares => shares.Count)
                .Subscribe(count => poolStats.ValidSharesPerMinute = count));

            disposables.Add(invalidShares
                .Buffer(TimeSpan.FromMinutes(1))
                .Select(shares => shares.Count)
                .Subscribe(count => poolStats.InvalidSharesPerMinute = count));
        }

        protected void ConsiderBan(StratumClient<TWorkerContext> client, WorkerContextBase context, PoolBanningConfig config)
        {
            var totalShares = context.Stats.ValidShares + context.Stats.InvalidShares;

            if (totalShares > config.CheckThreshold)
            {
                var ratioBad = (double) context.Stats.InvalidShares / totalShares;

                if (ratioBad < config.InvalidPercent / 100.0)
                {
                    // reset stats
                    context.Stats.ValidShares = 0;
                    context.Stats.InvalidShares = 0;
                }

                else
                {
                    logger.Warn(() => $"[{LogCat}] [{client.ConnectionId}] Banning worker for {config.Time} sec: {Math.Floor(ratioBad * 100)}% of the last {totalShares} shares were invalid");

                    banManager.Ban(client.RemoteEndpoint.Address, TimeSpan.FromSeconds(config.Time));

                    DisconnectClient(client);
                }
            }
        }

        private IPEndPoint PoolEndpoint2IPEndpoint(int port, PoolEndpoint pep)
        {
            var listenAddress = IPAddress.Parse("127.0.0.1");
            if (!string.IsNullOrEmpty(pep.ListenAddress))
                listenAddress = pep.ListenAddress != "*" ? IPAddress.Parse(pep.ListenAddress) : IPAddress.Any;

            return new IPEndPoint(listenAddress, port);
        }

        private void OutputPoolInfo()
        {
            var msg = $@"

Mining Pool:            {poolConfig.Id} 
Coin Type:		{poolConfig.Coin.Type} 
Network Connected:      {blockchainStats.NetworkType}
Detected Reward Type:   {blockchainStats.RewardType}
Current Block Height:   {blockchainStats.BlockHeight}
Current Connect Peers:  {blockchainStats.ConnectedPeers}
Network Difficulty:     {blockchainStats.NetworkDifficulty}
Network Hash Rate:      {FormatUtil.FormatHashRate(blockchainStats.NetworkHashRate)}
Stratum Port(s):        {string.Join(", ", poolConfig.Ports.Keys)}
Pool Fee:               {poolConfig.RewardRecipients.Sum(x => x.Percentage)}%
";

            logger.Info(() => msg);
        }

        protected virtual void SetupAdminNotifications()
        {
            if (clusterConfig.Notifications?.Admin?.Enabled == true)
            {
                if (clusterConfig.Notifications?.Admin?.NotifyBlockFound == true)
                {
                    var adminEmail = clusterConfig.Notifications.Admin.EmailAddress;

                    var emailSender = notificationSenders
                        .Where(x => x.Metadata.NotificationType == NotificationType.Email)
                        .Select(x => x.Value)
                        .First();

                    disposables.Add(Shares
                        .ObserveOn(TaskPoolScheduler.Default)
                        .Where(x => x.IsBlockCandidate)
                        .Subscribe(async share =>
                        {
                            try
                            {
                                await emailSender.NotifyAsync(adminEmail, "Block Notification", $"Pool {share.PoolId} found block candidate {share.BlockHeight}");
                            }

                            catch (Exception ex)
                            {
                                logger.Error(ex);
                            }
                        }));
                }
            }
        }

        protected abstract double CalculateHashrateForShares(IEnumerable<IShare> shares, int interval);

        protected virtual void UpdateMinerHashrates(IList<IShare> shares, int interval)
        {
            try
            {
                var sharesByMiner = shares.GroupBy(x => x.Miner);

                foreach (var minerShares in sharesByMiner)
                {
                    // Total hashrate
                    var miner = minerShares.Key;
                    var hashRate = CalculateHashrateForShares(minerShares, interval);

                    var sample = new MinerHashrateSample
                    {
                        PoolId = poolConfig.Id,
                        Miner = miner,
                        Hashrate = hashRate,
                        Created = DateTime.UtcNow
                    };

                    // Per worker hashrates
                    var sharesPerWorker = minerShares
                        .GroupBy(x => x.Worker)
                        .Where(x => !string.IsNullOrEmpty(x.Key));

                    foreach (var workerShares in sharesPerWorker)
                    {
                        var worker = workerShares.Key;
                        hashRate = CalculateHashrateForShares(workerShares, interval);

                        if(sample.WorkerHashrates == null)
                            sample.WorkerHashrates = new Dictionary<string, double>();

                        sample.WorkerHashrates[worker] = hashRate;
                    }

                    // Persist
                    cf.RunTx((con, tx) =>
                    {
                        statsRepo.RecordMinerHashrateSample(con, tx, sample);
                    });
                }
            }

            catch (Exception ex)
            {
                logger.Error(ex);
            }
        }

        #region API-Surface

        public IObservable<IShare> Shares { get; }
        public PoolConfig Config => poolConfig;
        public PoolStats PoolStats => poolStats;
        public BlockchainStats NetworkStats => blockchainStats;

        public virtual void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

            logger = LogUtil.GetPoolScopedLogger(typeof(PoolBase<TWorkerContext>), poolConfig);
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;
        }

        public virtual async Task StartAsync()
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

            logger.Info(() => $"[{LogCat}] Launching ...");

            try
            {
                SetupBanning(clusterConfig);
                SetupTelemetry();
                await SetupJobManager();

                var ipEndpoints = poolConfig.Ports.Keys
                    .Select(port => PoolEndpoint2IPEndpoint(port, poolConfig.Ports[port]))
                    .ToArray();

                StartListeners(ipEndpoints);
                SetupStats();
                SetupVarDiff();
                SetupAdminNotifications();

                logger.Info(() => $"[{LogCat}] Online");

                OutputPoolInfo();
            }

            catch (PoolStartupAbortException)
            {
                // just forward these
                throw;
            }

            catch (Exception ex)
            {
                logger.Error(ex);
                throw;
            }
        }

        #endregion // API-Surface
    }
}
