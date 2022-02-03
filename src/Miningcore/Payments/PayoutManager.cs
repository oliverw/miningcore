using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Autofac;
using Autofac.Features.Metadata;
using Microsoft.Extensions.Hosting;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Notifications.Messages;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Util;
using NLog;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Payments;

/// <summary>
/// Coin agnostic payment processor
/// </summary>
public class PayoutManager : BackgroundService
{
    public PayoutManager(IComponentContext ctx,
        IConnectionFactory cf,
        IBlockRepository blockRepo,
        IShareRepository shareRepo,
        IBalanceRepository balanceRepo,
        ClusterConfig clusterConfig,
        IMessageBus messageBus)
    {
        Contract.RequiresNonNull(ctx, nameof(ctx));
        Contract.RequiresNonNull(cf, nameof(cf));
        Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
        Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
        Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
        Contract.RequiresNonNull(messageBus, nameof(messageBus));
        Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

        this.ctx = ctx;
        this.cf = cf;
        this.blockRepo = blockRepo;
        this.shareRepo = shareRepo;
        this.balanceRepo = balanceRepo;
        this.messageBus = messageBus;
        this.clusterConfig = clusterConfig;

        interval = TimeSpan.FromSeconds(clusterConfig.PaymentProcessing.Interval > 0 ?
            clusterConfig.PaymentProcessing.Interval : 600);
    }

    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
    private readonly IBalanceRepository balanceRepo;
    private readonly IBlockRepository blockRepo;
    private readonly IConnectionFactory cf;
    private readonly IComponentContext ctx;
    private readonly IShareRepository shareRepo;
    private readonly IMessageBus messageBus;
    private readonly TimeSpan interval;
    private readonly ConcurrentDictionary<string, IMiningPool> pools = new();
    private readonly ClusterConfig clusterConfig;
    private readonly CompositeDisposable disposables = new();

    #if !DEBUG
    private static readonly TimeSpan initialRunDelay = TimeSpan.FromMinutes(1);
    #else
    private static readonly TimeSpan initialRunDelay = TimeSpan.FromSeconds(15);
    #endif

    private void AttachPool(IMiningPool pool)
    {
        pools.TryAdd(pool.Config.Id, pool);
    }

    private void OnPoolStatusNotification(PoolStatusNotification notification)
    {
        if(notification.Status == PoolStatus.Online)
            AttachPool(notification.Pool);
    }

    private async Task ProcessPoolsAsync(CancellationToken ct)
    {
        foreach(var pool in pools.Values.ToArray().Where(x => x.Config.Enabled && x.Config.PaymentProcessing.Enabled))
        {
            var config = pool.Config;

            logger.Info(() => $"Processing payments for pool {config.Id}");

            try
            {
                var handler = await ResolvePayoutHandlerAsync(pool, ct);

                // resolve payout scheme
                var scheme = ctx.ResolveKeyed<IPayoutScheme>(config.PaymentProcessing.PayoutScheme);

                await UpdatePoolBalancesAsync(pool, config, handler, scheme, ct);

                // telemetry
                var poolBalance = TelemetryUtil.TrackDependency(() => cf.Run(con => balanceRepo.GetTotalBalanceSum(con, pool.Config.Id, pool.Config.PaymentProcessing.MinimumPayment)),
                    DependencyType.Sql, "GetTotalBalanceSum", "GetTotalBalanceSum");
                var walletBalance = TelemetryUtil.TrackDependency(() => handler.GetWalletBalance(), DependencyType.Web3, "GetWalletBalance", "GetWalletBalance");
                await Task.WhenAll(poolBalance, walletBalance);

                TelemetryUtil.TrackEvent($"Balance_{pool.Config.Id}", new Dictionary<string, string>
                {
                    {"TotalBalance", poolBalance.Result.Sum(b => b.TotalAmount).ToStr()},
                    {"TotalOverThreshold", poolBalance.Result.Sum(b => b.TotalAmountOverThreshold).ToStr()},
                    {"WalletBalance", walletBalance.Result.ToStr()},
                    {"BalLT30Days", poolBalance.Result.FirstOrDefault(b=>b.NoOfDaysOld == 0)?.TotalAmount.ToStr()},
                    {"CusLT30Days", poolBalance.Result.FirstOrDefault(b=>b.NoOfDaysOld == 0)?.CustomersCount.ToString()},
                    {"BalGT30Days", poolBalance.Result.FirstOrDefault(b=>b.NoOfDaysOld == 30)?.TotalAmount.ToStr()},
                    {"CusGT30Days", poolBalance.Result.FirstOrDefault(b=>b.NoOfDaysOld == 30)?.CustomersCount.ToString()},
                    {"BalGT60Days", poolBalance.Result.FirstOrDefault(b=>b.NoOfDaysOld == 60)?.TotalAmount.ToStr()},
                    {"CusGT60Days", poolBalance.Result.FirstOrDefault(b=>b.NoOfDaysOld == 60)?.CustomersCount.ToString()},
                    {"BalGT90Days", poolBalance.Result.FirstOrDefault(b=>b.NoOfDaysOld == 90)?.TotalAmount.ToStr()},
                    {"CusGT90Days", poolBalance.Result.FirstOrDefault(b=>b.NoOfDaysOld == 90)?.CustomersCount.ToString()},
                });

                if(!clusterConfig.PaymentProcessing.OnDemandPayout)
                {
                    await PayoutPoolBalancesAsync(pool, config, handler, ct);
                }
            }
            catch(InvalidOperationException ex)
            {
                logger.Error(ex.InnerException ?? ex, () => $"[{config.Id}] Payment processing failed");
            }

            catch(AggregateException ex)
            {
                switch(ex.InnerException)
                {
                    case HttpRequestException httpEx:
                        logger.Error(() => $"[{config.Id}] Payment processing failed: {httpEx.Message}");
                        break;

                    default:
                        logger.Error(ex.InnerException, () => $"[{config.Id}] Payment processing failed");
                        break;
                }
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"[{config.Id}] Payment processing failed");
            }
        }
    }

    private static CoinFamily HandleFamilyOverride(CoinFamily family, PoolConfig pool)
    {
        switch(family)
        {
            case CoinFamily.Equihash:
                var equihashTemplate = pool.Template.As<EquihashCoinTemplate>();

                if(equihashTemplate.UseBitcoinPayoutHandler)
                    return CoinFamily.Bitcoin;

                break;
        }

        return family;
    }

    private async Task UpdatePoolBalancesAsync(IMiningPool pool, PoolConfig config, IPayoutHandler handler, IPayoutScheme scheme, CancellationToken ct)
    {
        // get pending blockRepo for pool
        var pendingBlocks = await TelemetryUtil.TrackDependency(() => cf.Run(con => blockRepo.GetPendingBlocksForPoolAsync(con, config.Id)),
                DependencyType.Sql, "getPendingBlocks", "getPendingBlocks");

        // classify
        var updatedBlocks = await handler.ClassifyBlocksAsync(pool, pendingBlocks, ct);

        if(updatedBlocks.Any())
        {
            foreach(var block in updatedBlocks.OrderBy(x => x.Created))
            {
                logger.Info(() => $"Processing payments for pool {config.Id}, block {block.BlockHeight}");

                await cf.RunTx(async (con, tx) =>
                {
                    if(!block.Effort.HasValue)  // fill block effort if empty
                        await CalculateBlockEffortAsync(pool, config, block, handler, ct);

                    switch(block.Status)
                    {
                        case BlockStatus.Confirmed:
                            // blockchains that do not support block-reward payments via coinbase Tx
                            // must generate balance records for all reward recipients instead
                            var blockReward = await handler.UpdateBlockRewardBalancesAsync(con, tx, pool, block, ct);

                            await scheme.UpdateBalancesAsync(con, tx, pool, handler, block, blockReward, ct);
                            await blockRepo.UpdateBlockAsync(con, tx, block);
                            break;

                        case BlockStatus.Orphaned:
                        case BlockStatus.Pending:
                            await blockRepo.UpdateBlockAsync(con, tx, block);
                            break;
                    }
                });
            }
        }

        else
        {
            logger.Info(() => $"No updated blocks for pool {config.Id}");
            await TelemetryUtil.TrackDependency(() => cf.RunTx(async (con, tx) =>
            {
                var blockReward = await handler.UpdateBlockRewardBalancesAsync(con, tx, pool, null, ct);
                await scheme.UpdateBalancesAsync(con, tx, pool, handler, null, blockReward, ct);
            }), DependencyType.Sql, "UpdateBalancesAsync", "UpdateBalances");
        }
    }

    private async Task PayoutPoolBalancesAsync(IMiningPool pool, PoolConfig config, IPayoutHandler handler, CancellationToken ct)
    {
        var poolBalancesOverMinimum = await TelemetryUtil.TrackDependency(() => cf.Run(con =>
            balanceRepo.GetPoolBalancesOverThresholdAsync(con, config.Id, config.PaymentProcessing.MinimumPayment)),
            DependencyType.Sql, "GetPoolBalancesOverThresholdAsync", "GetPoolBalancesOverThresholdAsync");

        if(poolBalancesOverMinimum.Length > 0)
        {
            try
            {
                await TelemetryUtil.TrackDependency(() => handler.PayoutAsync(pool, poolBalancesOverMinimum, ct),DependencyType.Sql, "PayoutPoolBalancesAsync",
                    $"miners:{poolBalancesOverMinimum.Length}");
            }

            catch(Exception ex)
            {
                logger.Error(ex, "Error while processing balance payout");
                await NotifyPayoutFailureAsync(poolBalancesOverMinimum, config, ex);
                throw;
            }
        }

        else
            logger.Info(() => $"No balances over configured minimum payout for pool {config.Id}");
    }

    private Task NotifyPayoutFailureAsync(Balance[] balances, PoolConfig pool, Exception ex)
    {
        messageBus.SendMessage(new PaymentNotification(pool.Id, ex.Message, balances.Sum(x => x.Amount), pool.Template.Symbol));

        return Task.FromResult(true);
    }

    private async Task CalculateBlockEffortAsync(IMiningPool pool, PoolConfig config, Block block, IPayoutHandler handler, CancellationToken ct)
    {
        // get share date-range
        var from = DateTime.MinValue;
        var to = block.Created;

        // get last block for pool
        var lastBlock = await cf.Run(con => blockRepo.GetBlockBeforeAsync(con, config.Id, new[]
        {
            BlockStatus.Confirmed,
            BlockStatus.Orphaned,
            BlockStatus.Pending,
        }, block.Created));

        if(lastBlock != null)
            from = lastBlock.Created;

        // get combined diff of all shares for block
        var accumulatedShareDiffForBlock = await cf.Run(con =>
            shareRepo.GetAccumulatedShareDifficultyBetweenCreatedAsync(con, config.Id, from, to, ct));

        // handler has the final say
        if(accumulatedShareDiffForBlock.HasValue)
            await handler.CalculateBlockEffortAsync(pool, block, accumulatedShareDiffForBlock.Value, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            // monitor pool lifetime
            disposables.Add(messageBus.Listen<PoolStatusNotification>()
                .ObserveOn(TaskPoolScheduler.Default)
                .Subscribe(OnPoolStatusNotification));

            logger.Info(() => "Online");

            // Allow all pools to actually come up before the first payment processing run
            await Task.Delay(initialRunDelay, ct);

            // Configure on-demand payout for each payout handler
            if(clusterConfig.PaymentProcessing.OnDemandPayout)
            {
                await ConfigureOnDemandPayoutAsync(ct);
            }

            while(!ct.IsCancellationRequested)
            {
                try
                {
                    await ProcessPoolsAsync(ct);
                }

                catch(Exception ex)
                {
                    logger.Error(ex);
                }

                await Task.Delay(interval, ct);
            }

            logger.Info(() => "Offline");
        }

        finally
        {
            disposables.Dispose();
        }
    }

    public async Task<string> PayoutSingleBalanceAsync(IMiningPool pool, string miner)
    {
        var success = true;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        decimal amount = 0;
        try
        {
            var handler = await ResolvePayoutHandlerAsync(pool, CancellationToken.None);

            var balance = await cf.Run(con => balanceRepo.GetBalanceDataWithPaidDateAsync(con, pool.Config.Id, miner));
            amount = balance.Amount;

            // payout a single balance
            return (await handler.PayoutAsync(balance))?.Id;
        }
        catch(Exception ex)
        {
            logger.Error($"Failed to payout {miner}: {ex}");
            success = false;
            throw;
        }
        finally
        {
            TelemetryUtil.TrackMetric("FORCED_PAYOUT", "success", "duration", (double) amount, success.ToString(), timer.ElapsedMilliseconds.ToString());
        }
    }

    private async Task<IPayoutHandler> ResolvePayoutHandlerAsync(IMiningPool pool, CancellationToken ct)
    {
        var family = HandleFamilyOverride(pool.Config.Template.Family, pool.Config);

        // resolve payout handler
        var handlerImpl = ctx.Resolve<IEnumerable<Meta<Lazy<IPayoutHandler, CoinFamilyAttribute>>>>()
            .First(x => x.Value.Metadata.SupportedFamilies.Contains(family)).Value;

        var handler = handlerImpl.Value;
        await handler.ConfigureAsync(clusterConfig, pool.Config, ct);
        return handler;
    }

    private async Task ConfigureOnDemandPayoutAsync(CancellationToken ct)
    {
        foreach(var pool in pools.Values.ToArray().Where(x => x.Config.Enabled && x.Config.PaymentProcessing.Enabled))
        {
            logger.Info(() => $"Configuring on-demand payouts for pool {pool.Config.Id}");

            var handler = await ResolvePayoutHandlerAsync(pool, ct);

            try
            {
                handler.ConfigureOnDemandPayoutAsync(ct);
            }
            catch(Exception ex)
            {
                logger.Error(ex, () => $"Configuring on-demand payout failed for pool [{pool.Config.Id}]");
                throw;
            }
        }
    }
}
