using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using Autofac;
using AutoMapper;
using Microsoft.IO;
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
using static Miningcore.Util.ActionUtils;

// ReSharper disable InconsistentlySynchronizedField

namespace Miningcore.Mining;

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
        RecyclableMemoryStreamManager rmsm,
        NicehashService nicehashService) : base(ctx, messageBus, rmsm, clock)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(serializerSettings);
        Contract.RequiresNonNull(cf);
        Contract.RequiresNonNull(statsRepo);
        Contract.RequiresNonNull(mapper);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(messageBus);
        Contract.RequiresNonNull(nicehashService);

        this.serializerSettings = serializerSettings;
        this.cf = cf;
        this.statsRepo = statsRepo;
        this.mapper = mapper;
        this.nicehashService = nicehashService;
    }

    protected PoolStats poolStats = new();
    protected readonly JsonSerializerSettings serializerSettings;
    protected readonly IConnectionFactory cf;
    protected readonly IStatsRepository statsRepo;
    protected readonly IMapper mapper;
    protected readonly NicehashService nicehashService;
    protected readonly CompositeDisposable disposables = new();
    protected BlockchainStats blockchainStats;
    protected static readonly TimeSpan maxShareAge = TimeSpan.FromSeconds(6);
    protected static readonly TimeSpan loginFailureBanTimeout = TimeSpan.FromSeconds(10);
    protected static readonly Regex regexStaticDiff = new(@";?d=(\d*(\.\d+)?)", RegexOptions.Compiled);
    protected const string PasswordControlVarsSeparator = ";";

    protected abstract Task SetupJobManager(CancellationToken ct);
    protected abstract WorkerContextBase CreateWorkerContext();

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
        // setup context
        var context = CreateWorkerContext();
        var poolEndpoint = poolConfig.Ports[ipEndPoint.Port];
        var varDiff = poolConfig.EnableInternalStratum == true ? poolEndpoint.VarDiff : null;

        context.Init(poolEndpoint.Difficulty, varDiff, clock);
        connection.SetContext(context);

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

                        Disconnect(connection);
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

    #region VarDiff

    protected async Task UpdateVarDiffAsync(StratumConnection connection, bool idle, CancellationToken ct)
    {
        var context = connection.Context;

        if(context.VarDiff != null)
        {
            logger.Debug(() => $"[{connection.ConnectionId}] Updating VarDiff{(idle ? " [IDLE]" : "")}");

            var poolEndpoint = poolConfig.Ports[connection.LocalEndpoint.Port];

            var newDiff = !idle ?
                VarDiffManager.Update(context, poolEndpoint.VarDiff, clock) :
                VarDiffManager.IdleUpdate(context, poolEndpoint.VarDiff, clock);

            if(newDiff != null)
            {
                logger.Info(() => $"[{connection.ConnectionId}] VarDiff update to {Math.Round(newDiff.Value, 3)}{(idle ? " [IDLE]" : "")}");

                await OnVarDiffUpdateAsync(connection, newDiff.Value, ct);
            }
        }
    }

    private async Task RunVardiffIdleUpdaterAsync(int interval, CancellationToken ct)
    {
        await Guard(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));

            while(await timer.WaitForNextTickAsync(ct))
            {
                logger.Debug(() => "Vardiff Idle Update pass begins");

                await Guard(() => ForEachMinerAsync(async (connection, _ct) =>
                {
                    await Guard(() => UpdateVarDiffAsync(connection, true, _ct),
                        ex => logger.Error(() => $"[{connection.ConnectionId}] Error updating vardiff: {ex.Message}"));
                }, ct));

                logger.Debug(() => "Vardiff Idle Update pass ends");
            }
        }, ex =>
        {
            if(ex is not OperationCanceledException)
                logger.Error(ex);
        });
    }

    protected virtual Task OnVarDiffUpdateAsync(StratumConnection connection, double newDiff, CancellationToken _)
    {
        connection.Context.EnqueueNewDifficulty(newDiff);

        return Task.CompletedTask;
    }

    #endregion // VarDiff

    protected Task ForEachMinerAsync(Func<StratumConnection, CancellationToken, Task> func)
    {
        return ForEachMinerAsync(func, CancellationToken.None);
    }

    protected async Task ForEachMinerAsync(Func<StratumConnection, CancellationToken, Task> func, CancellationToken ct)
    {
        await Parallel.ForEachAsync(connections, ct, async (kvp, _ct) =>
        {
            var connection = kvp.Value;

            try
            {
                if(!_ct.IsCancellationRequested && connection.IsAlive && connection.Context.IsAuthorized)
                {
                    ZombieCheck(connection);

                    await func(connection, _ct);
                }
            }

            catch(Exception ex)
            {
                logger.Error(() => $"[{connection.ConnectionId}] {LogUtil.DotTerminate(ex.Message)} Closing connection ...");

                Disconnect(connection);
            }
        });
    }

    protected void ZombieCheck(StratumConnection connection)
    {
        if(poolConfig.ClientConnectionTimeout > 0)
        {
            var lastActivityAgo = clock.Now - connection.Context.LastActivity;

            if(lastActivityAgo.TotalSeconds > poolConfig.ClientConnectionTimeout)
                throw new Exception($"Detected zombie-worker (idle-timeout exceeded)");
        }
    }

    protected void SetupBanManagement()
    {
        if(poolConfig.Banning?.Enabled == true)
        {
            var managerType = clusterConfig.Banning?.Manager ?? BanManagerKind.Integrated;
            banManager = ctx.ResolveKeyed<IBanManager>(managerType);
        }
    }

    protected virtual async Task InitStatsAsync(CancellationToken ct)
    {
        if(clusterConfig.ShareRelay == null)
            await LoadStatsAsync(ct);
    }

    private async Task LoadStatsAsync(CancellationToken ct)
    {
        try
        {
            logger.Debug(() => "Loading pool stats");

            var stats = await cf.Run(con => statsRepo.GetLastPoolStatsAsync(con, poolConfig.Id, ct));

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

                    Disconnect(connection);
                }
            }
        }
    }

    protected async Task RunStratum(CancellationToken ct)
    {
        var ipEndpoints = poolConfig.Ports.Keys
            .Select(port => PoolEndpoint2IPEndpoint(port, poolConfig.Ports[port]))
            .ToArray();

        var varDiffEnabled = ipEndpoints.Any(x => x.PoolEndpoint.VarDiff != null);

        var tasks = new List<Task>
        {
            base.RunAsync(ct, ipEndpoints)
        };

        if(varDiffEnabled)
            tasks.Add(RunVardiffIdleUpdaterAsync(poolConfig.VardiffIdleSweepInterval ?? 30, ct));

        await Task.WhenAll(tasks);
    }

    protected virtual async Task<double?> GetNicehashStaticMinDiff(WorkerContextBase context, string coinName, string algoName)
    {
        if(context.IsNicehash && clusterConfig.Nicehash?.EnableAutoDiff == true)
            return await nicehashService.GetStaticDiff(coinName, algoName, CancellationToken.None);

        return null;
    }

    private StratumEndpoint PoolEndpoint2IPEndpoint(int port, PoolEndpoint pep)
    {
        var listenAddress = IPAddress.Parse("127.0.0.1");
        if(!string.IsNullOrEmpty(pep.ListenAddress))
            listenAddress = pep.ListenAddress != "*" ? IPAddress.Parse(pep.ListenAddress) : IPAddress.Any;

        return new StratumEndpoint(new IPEndPoint(listenAddress, port), pep);
    }

    private void LogPoolInfo()
    {
        logger.Info(() => "Pool Online");

        var msg = $@"

Mining Pool:            {poolConfig.Id}
Coin Type:              {poolConfig.Template.Symbol} [{poolConfig.Template.Symbol}]
Network Connected:      {blockchainStats.NetworkType}
Detected Reward Type:   {blockchainStats.RewardType}
Current Block Height:   {blockchainStats.BlockHeight}
Current Connect Peers:  {blockchainStats.ConnectedPeers}
Network Difficulty:     {(blockchainStats.NetworkDifficulty > 1000 ? FormatUtil.FormatQuantity(blockchainStats.NetworkDifficulty) : blockchainStats.NetworkDifficulty)}
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

    public virtual void Configure(PoolConfig pc, ClusterConfig cc)
    {
        Contract.RequiresNonNull(pc);
        Contract.RequiresNonNull(cc);

        logger = LogUtil.GetPoolScopedLogger(typeof(PoolBase), pc);
        poolConfig = pc;
        clusterConfig = cc;
    }

    public abstract double HashrateFromShares(double shares, double interval);
    public virtual double ShareMultiplier => 1;

    public virtual async Task RunAsync(CancellationToken ct)
    {
        Contract.RequiresNonNull(poolConfig);

        logger.Info(() => "Starting Pool ...");

        try
        {
            SetupBanManagement();

            await SetupJobManager(ct);
            await InitStatsAsync(ct);

            LogPoolInfo();

            messageBus.NotifyPoolStatus(this, PoolStatus.Online);

            if(poolConfig.EnableInternalStratum == true)
                await RunStratum(ct);
        }

        catch(PoolStartupException)
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
