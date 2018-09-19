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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningCore.Banning;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Messaging;
using MiningCore.Notifications;
using MiningCore.Notifications.Messages;
using MiningCore.Persistence;
using MiningCore.Persistence.Repositories;
using MiningCore.Stratum;
using MiningCore.Time;
using MiningCore.Util;
using MiningCore.VarDiff;
using Newtonsoft.Json;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Mining
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
            IMessageBus messageBus) : base(ctx, clock)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));

            this.serializerSettings = serializerSettings;
            this.cf = cf;
            this.statsRepo = statsRepo;
            this.mapper = mapper;
            this.messageBus = messageBus;
        }

        protected PoolStats poolStats = new PoolStats();
        protected readonly JsonSerializerSettings serializerSettings;
        protected readonly IConnectionFactory cf;
        protected readonly IStatsRepository statsRepo;
        protected readonly IMapper mapper;
        protected readonly IMessageBus messageBus;
        protected readonly CompositeDisposable disposables = new CompositeDisposable();
        protected BlockchainStats blockchainStats;
        protected PoolConfig poolConfig;
        protected static readonly TimeSpan maxShareAge = TimeSpan.FromSeconds(6);
        protected static readonly Regex regexStaticDiff = new Regex(@"d=(\d*(\.\d+)?)", RegexOptions.Compiled);
        protected const string PasswordControlVarsSeparator = ";";

        protected readonly Dictionary<PoolEndpoint, VarDiffManager> varDiffManagers =
            new Dictionary<PoolEndpoint, VarDiffManager>();

        protected override string LogCat => "Pool";

        protected abstract Task SetupJobManager(CancellationToken ct);
        protected abstract WorkerContextBase CreateClientContext();

        protected double? GetStaticDiffFromPassparts(string[] parts)
        {
            if (parts == null || parts.Length == 0)
                return null;

            foreach(var part in parts)
            {
                var m = regexStaticDiff.Match(part);

                if (m.Success)
                {
                    var str = m.Groups[1].Value.Trim();
                    if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var diff))
                        return diff;
                }
            }

            return null;
        }

        protected override void OnConnect(StratumClient client, IPEndPoint ipEndPoint)
        {
            // client setup
            var context = CreateClientContext();

            var poolEndpoint = poolConfig.Ports[ipEndPoint.Port];
            context.Init(poolConfig, poolEndpoint.Difficulty, poolConfig.EnableInternalStratum == true ? poolEndpoint.VarDiff : null, clock);
            client.SetContext(context);

            // varDiff setup
            if (context.VarDiff != null)
            {
                lock(context.VarDiff)
                {
                    StartVarDiffIdleUpdate(client, poolEndpoint);
                }
            }

            // expect miner to establish communication within a certain time
            EnsureNoZombieClient(client);
        }

        private void EnsureNoZombieClient(StratumClient client)
        {
            Observable.Timer(clock.Now.AddSeconds(10))
                .Take(1)
                .Subscribe(_ =>
                {
                    if (!client.LastReceive.HasValue)
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (post-connect silence)");

                        DisconnectClient(client);
                    }
                });
        }

        protected void PublishTelemetry(TelemetryCategory cat, TimeSpan elapsed, bool? success = null)
        {
            messageBus.SendMessage(new TelemetryEvent(clusterConfig.ClusterName ?? poolConfig.PoolName, poolConfig.Id, cat, elapsed, success));
        }

        #region VarDiff

        protected async Task UpdateVarDiffAsync(StratumClient client, bool isIdleUpdate = false)
        {
            var context = client.ContextAs<WorkerContextBase>();

            if (context.VarDiff != null)
            {
                logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Updating VarDiff" + (isIdleUpdate ? " [idle]" : string.Empty));

                // get or create manager
                VarDiffManager varDiffManager;
                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                lock(varDiffManagers)
                {
                    if (!varDiffManagers.TryGetValue(poolEndpoint, out varDiffManager))
                    {
                        varDiffManager = new VarDiffManager(poolEndpoint.VarDiff, clock);
                        varDiffManagers[poolEndpoint] = varDiffManager;
                    }
                }

                double? newDiff = null;

                lock(context.VarDiff)
                {
                    StartVarDiffIdleUpdate(client, poolEndpoint);

                    // update it
                    newDiff = varDiffManager.Update(context.VarDiff, context.Difficulty, isIdleUpdate);
                }

                if (newDiff != null)
                {
                    logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] VarDiff update to {Math.Round(newDiff.Value, 2)}");

                    await OnVarDiffUpdateAsync(client, newDiff.Value);
                }
            }
        }

        /// <summary>
        /// Wire interval based vardiff updates for client
        /// WARNING: Assumes to be invoked with lock held on context.VarDiff
        /// </summary>
        private void StartVarDiffIdleUpdate(StratumClient client, PoolEndpoint poolEndpoint)
        {
            // Check Every Target Time as we adjust the diff to meet target
            // Diff may not be changed , only be changed when avg is out of the range.
            // Diff must be dropped once changed. Will not affect reject rate.
            var interval = poolEndpoint.VarDiff.TargetTime;
            var shareReceivedFromClient = messageBus.Listen<ClientShare>().Where(x => x.Share.PoolId == poolConfig.Id && x.Client == client);

            Observable
                .Timer(TimeSpan.FromSeconds(interval))
                .TakeUntil(shareReceivedFromClient)
                .Take(1)
                .Where(x => client.IsAlive)
                .Select(x => Observable.FromAsync(() => UpdateVarDiffAsync(client, true)))
                .Concat()
                .Subscribe();
        }

        protected virtual Task OnVarDiffUpdateAsync(StratumClient client, double newDiff)
        {
            var context = client.ContextAs<WorkerContextBase>();
            context.EnqueueNewDifficulty(newDiff);

            return Task.FromResult(true);
        }

        #endregion // VarDiff

        protected void SetupBanning(ClusterConfig clusterConfig)
        {
            if (poolConfig.Banning?.Enabled == true)
            {
                var managerType = clusterConfig.Banning?.Manager ?? BanManagerKind.Integrated;
                banManager = ctx.ResolveKeyed<IBanManager>(managerType);
            }
        }

        protected virtual void InitStats()
        {
            if (clusterConfig.ShareRelay == null)
                LoadStats();
        }

        private void LoadStats()
        {
            try
            {
                logger.Debug(() => $"[{LogCat}] Loading pool stats");

                var stats = cf.Run(con => statsRepo.GetLastPoolStats(con, poolConfig.Id));

                if (stats != null)
                {
                    poolStats = mapper.Map<PoolStats>(stats);
                    blockchainStats = mapper.Map<BlockchainStats>(stats);
                }
            }

            catch(Exception ex)
            {
                logger.Warn(ex, () => $"[{LogCat}] Unable to load pool stats");
            }
        }

        protected void ConsiderBan(StratumClient client, WorkerContextBase context, PoolShareBasedBanningConfig config)
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
                    if (poolConfig.Banning?.Enabled == true &&
                        (clusterConfig.Banning?.BanOnInvalidShares.HasValue == false ||
                            clusterConfig.Banning?.BanOnInvalidShares == true))
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Banning worker for {config.Time} sec: {Math.Floor(ratioBad * 100)}% of the last {totalShares} shares were invalid");

                        banManager.Ban(client.RemoteEndpoint.Address, TimeSpan.FromSeconds(config.Time));

                        DisconnectClient(client);
                    }
                }
            }
        }

        private (IPEndPoint IPEndPoint, PoolEndpoint PoolEndpoint) PoolEndpoint2IPEndpoint(int port, PoolEndpoint pep)
        {
            var listenAddress = IPAddress.Parse("127.0.0.1");
            if (!string.IsNullOrEmpty(pep.ListenAddress))
                listenAddress = pep.ListenAddress != "*" ? IPAddress.Parse(pep.ListenAddress) : IPAddress.Any;

            return (new IPEndPoint(listenAddress, port), pep);
        }

        private void OutputPoolInfo()
        {
            var msg = $@"

Mining Pool:            {poolConfig.Id}
Coin Type:              {poolConfig.Coin.Type}
Network Connected:      {blockchainStats.NetworkType}
Detected Reward Type:   {blockchainStats.RewardType}
Current Block Height:   {blockchainStats.BlockHeight}
Current Connect Peers:  {blockchainStats.ConnectedPeers}
Network Difficulty:     {blockchainStats.NetworkDifficulty}
Network Hash Rate:      {FormatUtil.FormatHashrate(blockchainStats.NetworkHashrate)}
Stratum Port(s):        {(poolConfig.Ports?.Any() == true ? string.Join(", ", poolConfig.Ports.Keys) : string.Empty)}
Pool Fee:               {(poolConfig.RewardRecipients?.Any() == true ? poolConfig.RewardRecipients.Sum(x => x.Percentage) : 0)}%
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

        public virtual async Task StartAsync(CancellationToken ct)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

            logger.Info(() => $"[{LogCat}] Launching ...");

            try
            {
                SetupBanning(clusterConfig);
                await SetupJobManager(ct);
                InitStats();

                if (poolConfig.EnableInternalStratum == true)
                {
                    var ipEndpoints = poolConfig.Ports.Keys
                        .Select(port => PoolEndpoint2IPEndpoint(port, poolConfig.Ports[port]))
                        .ToArray();

                    StartListeners(ipEndpoints);
                }

                logger.Info(() => $"[{LogCat}] Online");
                OutputPoolInfo();
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

        public void Stop()
        {
            StopListeners();
        }

        #endregion // API-Surface
    }
}
