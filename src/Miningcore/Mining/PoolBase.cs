using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using Miningcore.Banning;
using Miningcore.Blockchain;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Nicehash;
using Miningcore.Notifications.Messages;
using Miningcore.Persistence;
using Miningcore.Persistence.Repositories;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using Miningcore.VarDiff;
using Newtonsoft.Json;
using NLog;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Mining
{
    public abstract class PoolBase : StratumServer,
        IMiningPool
    {
        protected PoolBase(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            IMessageBus messageBus,
            NicehashService nicehashService) : base(ctx, clock)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));
            Contract.RequiresNonNull(nicehashService, nameof(nicehashService));

            this.serializerSettings = serializerSettings;
            this.cf = cf;
            this.statsRepo = statsRepo;
            this.mapper = mapper;
            this.messageBus = messageBus;
            this.nicehashService = nicehashService;
        }

        protected PoolStats poolStats = new();
        protected readonly JsonSerializerSettings serializerSettings;
        protected readonly IConnectionFactory cf;
        protected readonly IStatsRepository statsRepo;
        protected readonly IMapper mapper;
        protected readonly IMessageBus messageBus;
        protected readonly NicehashService nicehashService;
        protected readonly CompositeDisposable disposables = new();
        protected BlockchainStats blockchainStats;
        protected PoolConfig poolConfig;
        protected static readonly TimeSpan maxShareAge = TimeSpan.FromSeconds(6);
        protected static readonly Regex regexStaticDiff = new(@";?d=(\d*(\.\d+)?)", RegexOptions.Compiled);
        protected const string PasswordControlVarsSeparator = ";";

        protected readonly Dictionary<PoolEndpoint, VarDiffManager> varDiffManagers =
            new();

        protected abstract Task SetupJobManager(CancellationToken ct);
        protected abstract WorkerContextBase CreateClientContext();

        protected double? GetStaticDiffFromPassparts(string[] parts)
        {
            if(parts == null || parts.Length == 0)
                return null;

            foreach(var part in parts)
            {
                var m = regexStaticDiff.Match(part);

                if(m.Success)
                {
                    var str = m.Groups[1].Value.Trim();
                    if(double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var diff) &&
                        !double.IsNaN(diff) && !double.IsInfinity(diff))
                        return diff;
                }
            }

            return null;
        }

        protected override void OnConnect(StratumConnection connection, IPEndPoint ipEndPoint)
        {
            // client setup
            var context = CreateClientContext();

            var poolEndpoint = poolConfig.Ports[ipEndPoint.Port];
            context.Init(poolConfig, poolEndpoint.Difficulty, poolConfig.EnableInternalStratum == true ? poolEndpoint.VarDiff : null, clock);
            connection.SetContext(context);

            // varDiff setup
            if(context.VarDiff != null)
            {
                lock(context.VarDiff)
                {
                    StartVarDiffIdleUpdate(connection, poolEndpoint);
                }
            }

            // expect miner to establish communication within a certain time
            EnsureNoZombieClient(connection);
        }

        private void EnsureNoZombieClient(StratumConnection connection)
        {
            Observable.Timer(clock.Now.AddSeconds(10))
                .TakeUntil(connection.Terminated)
                .Where(_ => connection.IsAlive)
                .Subscribe(_ =>
                {
                    try
                    {
                        if(connection.LastReceive == null)
                        {
                            logger.Info(() => $"[{connection.ConnectionId}] Booting zombie-worker (post-connect silence)");

                            CloseConnection(connection);
                        }
                    }

                    catch(Exception ex)
                    {
                        logger.Error(ex);
                    }
                }, ex =>
                {
                    logger.Error(ex, nameof(EnsureNoZombieClient));
                });
        }

        protected void PublishTelemetry(TelemetryCategory cat, TimeSpan elapsed, bool? success = null)
        {
            messageBus.SendMessage(new TelemetryEvent(clusterConfig.ClusterName ?? poolConfig.PoolName, poolConfig.Id, cat, elapsed, success));
        }

        #region VarDiff

        protected async Task UpdateVarDiffAsync(StratumConnection connection, bool isIdleUpdate = false)
        {
            var context = connection.ContextAs<WorkerContextBase>();

            if(context.VarDiff != null)
            {
                logger.Debug(() => $"[{connection.ConnectionId}] Updating VarDiff" + (isIdleUpdate ? " [idle]" : string.Empty));

                // get or create manager
                VarDiffManager varDiffManager;
                var poolEndpoint = poolConfig.Ports[connection.PoolEndpoint.Port];

                lock(varDiffManagers)
                {
                    if(!varDiffManagers.TryGetValue(poolEndpoint, out varDiffManager))
                    {
                        varDiffManager = new VarDiffManager(poolEndpoint.VarDiff, clock);
                        varDiffManagers[poolEndpoint] = varDiffManager;
                    }
                }

                double? newDiff = null;

                lock(context.VarDiff)
                {
                    StartVarDiffIdleUpdate(connection, poolEndpoint);

                    // update it
                    newDiff = varDiffManager.Update(context.VarDiff, context.Difficulty, isIdleUpdate);
                }

                if(newDiff != null)
                {
                    logger.Info(() => $"[{connection.ConnectionId}] VarDiff update to {Math.Round(newDiff.Value, 3)}");

                    await OnVarDiffUpdateAsync(connection, newDiff.Value);
                }
            }
        }

        /// <summary>
        /// Wire interval based vardiff updates for client
        /// WARNING: Assumes to be invoked with lock held on context.VarDiff
        /// </summary>
        private void StartVarDiffIdleUpdate(StratumConnection connection, PoolEndpoint poolEndpoint)
        {
            // Check Every Target Time as we adjust the diff to meet target
            // Diff may not be changed, only be changed when avg is out of the range.
            // Diff must be dropped once changed. Will not affect reject rate.

            var shareReceived = messageBus.Listen<ClientShare>()
                .Where(x => x.Share.PoolId == poolConfig.Id && x.Connection == connection)
                .Select(_ => Unit.Default)
                .Take(1);

            var timeout = poolEndpoint.VarDiff.TargetTime;

            Observable.Timer(TimeSpan.FromSeconds(timeout))
                .TakeUntil(Observable.Merge(shareReceived, connection.Terminated))
                .Where(_ => connection.IsAlive)
                .Select(x => Observable.FromAsync(() => UpdateVarDiffAsync(connection, true)))
                .Concat()
                .Subscribe(_ => { }, ex =>
                {
                    logger.Debug(ex, nameof(StartVarDiffIdleUpdate));
                });
        }

        protected virtual Task OnVarDiffUpdateAsync(StratumConnection connection, double newDiff)
        {
            var context = connection.ContextAs<WorkerContextBase>();
            context.EnqueueNewDifficulty(newDiff);

            return Task.FromResult(true);
        }

        #endregion // VarDiff

        protected void SetupBanning(ClusterConfig clusterConfig)
        {
            if(poolConfig.Banning?.Enabled == true)
            {
                var managerType = clusterConfig.Banning?.Manager ?? BanManagerKind.Integrated;
                banManager = ctx.ResolveKeyed<IBanManager>(managerType);
            }
        }

        protected virtual async Task InitStatsAsync()
        {
            if(clusterConfig.ShareRelay == null)
                await LoadStatsAsync();
        }

        private async Task LoadStatsAsync()
        {
            try
            {
                logger.Debug(() => "Loading pool stats");

                var stats = await cf.Run(con => statsRepo.GetLastPoolStatsAsync(con, poolConfig.Id));

                if(stats != null)
                {
                    poolStats = mapper.Map<PoolStats>(stats);
                    blockchainStats = mapper.Map<BlockchainStats>(stats);
                }
            }

            catch(Exception ex)
            {
                logger.Warn(ex, () => "Unable to load pool stats");
            }
        }

        protected void ConsiderBan(StratumConnection connection, WorkerContextBase context, PoolShareBasedBanningConfig config)
        {
            var totalShares = context.Stats.ValidShares + context.Stats.InvalidShares;

            if(totalShares > config.CheckThreshold)
            {
                var ratioBad = (double) context.Stats.InvalidShares / totalShares;

                if(ratioBad < config.InvalidPercent / 100.0)
                {
                    // reset stats
                    context.Stats.ValidShares = 0;
                    context.Stats.InvalidShares = 0;
                }

                else
                {
                    if(poolConfig.Banning?.Enabled == true &&
                        (clusterConfig.Banning?.BanOnInvalidShares.HasValue == false ||
                            clusterConfig.Banning?.BanOnInvalidShares == true))
                    {
                        logger.Info(() => $"[{connection.ConnectionId}] Banning worker for {config.Time} sec: {Math.Floor(ratioBad * 100)}% of the last {totalShares} shares were invalid");

                        banManager.Ban(connection.RemoteEndpoint.Address, TimeSpan.FromSeconds(config.Time));

                        CloseConnection(connection);
                    }
                }
            }
        }

        private StratumEndpoint PoolEndpoint2IPEndpoint(int port, PoolEndpoint pep)
        {
            var listenAddress = IPAddress.Parse("127.0.0.1");
            if(!string.IsNullOrEmpty(pep.ListenAddress))
                listenAddress = pep.ListenAddress != "*" ? IPAddress.Parse(pep.ListenAddress) : IPAddress.Any;

            return new StratumEndpoint(new IPEndPoint(listenAddress, port), pep);
        }

        private void OutputPoolInfo()
        {
            var msg = $@"

Mining Pool:            {poolConfig.Id}
Coin Type:              {poolConfig.Template.Symbol} [{poolConfig.Template.Symbol}]
Network Connected:      {blockchainStats.NetworkType}
Detected Reward Type:   {blockchainStats.RewardType}
Current Block Height:   {blockchainStats.BlockHeight}
Current Connect Peers:  {blockchainStats.ConnectedPeers}
Network Difficulty:     {blockchainStats.NetworkDifficulty}
Network Hash Rate:      {FormatUtil.FormatHashrate(blockchainStats.NetworkHashrate)}
Stratum Port(s):        {(poolConfig.Ports?.Any() == true ? string.Join(", ", poolConfig.Ports.Keys) : string.Empty)}
Pool Fee:               {(poolConfig.RewardRecipients?.Any() == true ? poolConfig.RewardRecipients.Where(x => x.Type != "dev").Sum(x => x.Percentage) : 0)}%
";

            logger.Info(() => msg);
        }

        #region API-Surface

        public PoolConfig Config => poolConfig;
        public PoolStats PoolStats => poolStats;
        public BlockchainStats NetworkStats => blockchainStats;

        public virtual void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

            logger = LogUtil.GetPoolScopedLogger(typeof(PoolBase), poolConfig);
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;
        }

        public abstract double HashrateFromShares(double shares, double interval);

        public virtual async Task RunAsync(CancellationToken ct)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

            logger.Info(() => "Starting Pool ...");

            try
            {
                SetupBanning(clusterConfig);
                await SetupJobManager(ct);
                await InitStatsAsync();

                logger.Info(() => "Pool Online");
                OutputPoolInfo();

                messageBus.NotifyPoolStatus(this, PoolStatus.Online);

                if(poolConfig.EnableInternalStratum == true)
                {
                    var ipEndpoints = poolConfig.Ports.Keys
                        .Select(port => PoolEndpoint2IPEndpoint(port, poolConfig.Ports[port]))
                        .ToArray();

                    await ServeStratum(ct, ipEndpoints);
                }

                messageBus.NotifyPoolStatus(this, PoolStatus.Offline);
                logger.Info(() => "Pool Offline");
            }

            catch(PoolStartupAbortException)
            {
                // just forward these
                throw;
            }

            catch(TaskCanceledException)
            {
                // just forward these
                throw;
            }

            catch(Exception ex)
            {
                logger.Error(ex);
                throw;
            }
        }

        #endregion // API-Surface
    }
}
