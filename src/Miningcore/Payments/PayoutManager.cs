using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using NLog;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Payments
{
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
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private void AttachPool(IMiningPool pool)
        {
            pools.TryAdd(pool.Config.Id, pool);
        }

        private void OnPoolStatusNotification(PoolStatusNotification notification)
        {
            if(notification.Status == PoolStatus.Online)
                AttachPool(notification.Pool);
        }

        private async Task ProcessPoolsAsync()
        {
            foreach(var pool in pools.Values.ToArray().Select(x=> x.Config).Where(x => x.Enabled && x.PaymentProcessing.Enabled))
            {
                logger.Info(() => $"Processing payments for pool {pool.Id}");

                try
                {
                    var family = HandleFamilyOverride(pool.Template.Family, pool);

                    // resolve payout handler
                    var handlerImpl = ctx.Resolve<IEnumerable<Meta<Lazy<IPayoutHandler, CoinFamilyAttribute>>>>()
                        .First(x => x.Value.Metadata.SupportedFamilies.Contains(family)).Value;

                    var handler = handlerImpl.Value;
                    await handler.ConfigureAsync(clusterConfig, pool);

                    // resolve payout scheme
                    var scheme = ctx.ResolveKeyed<IPayoutScheme>(pool.PaymentProcessing.PayoutScheme);

                    await UpdatePoolBalancesAsync(pool, handler, scheme);
                    await PayoutPoolBalancesAsync(pool, handler);
                }

                catch(InvalidOperationException ex)
                {
                    logger.Error(ex.InnerException ?? ex, () => $"[{pool.Id}] Payment processing failed");
                }

                catch(AggregateException ex)
                {
                    switch(ex.InnerException)
                    {
                        case HttpRequestException httpEx:
                            logger.Error(() => $"[{pool.Id}] Payment processing failed: {httpEx.Message}");
                            break;

                        default:
                            logger.Error(ex.InnerException, () => $"[{pool.Id}] Payment processing failed");
                            break;
                    }
                }

                catch(Exception ex)
                {
                    logger.Error(ex, () => $"[{pool.Id}] Payment processing failed");
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

        private async Task UpdatePoolBalancesAsync(PoolConfig pool, IPayoutHandler handler, IPayoutScheme scheme)
        {
            // get pending blockRepo for pool
            var pendingBlocks = await cf.Run(con => blockRepo.GetPendingBlocksForPoolAsync(con, pool.Id));

            // classify
            var updatedBlocks = await handler.ClassifyBlocksAsync(pendingBlocks);

            if(updatedBlocks.Any())
            {
                foreach(var block in updatedBlocks.OrderBy(x => x.Created))
                {
                    logger.Info(() => $"Processing payments for pool {pool.Id}, block {block.BlockHeight}");

                    await cf.RunTx(async (con, tx) =>
                    {
                        if(!block.Effort.HasValue)  // fill block effort if empty
                            await CalculateBlockEffortAsync(pool, block, handler);

                        switch(block.Status)
                        {
                            case BlockStatus.Confirmed:
                                // blockchains that do not support block-reward payments via coinbase Tx
                                // must generate balance records for all reward recipients instead
                                var blockReward = await handler.UpdateBlockRewardBalancesAsync(con, tx, block, pool);

                                await scheme.UpdateBalancesAsync(con, tx, pool, handler, block, blockReward);
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
                logger.Info(() => $"No updated blocks for pool {pool.Id}");
        }

        private async Task PayoutPoolBalancesAsync(PoolConfig pool, IPayoutHandler handler)
        {
            var poolBalancesOverMinimum = await cf.Run(con =>
                balanceRepo.GetPoolBalancesOverThresholdAsync(con, pool.Id, pool.PaymentProcessing.MinimumPayment));

            if(poolBalancesOverMinimum.Length > 0)
            {
                try
                {
                    await handler.PayoutAsync(poolBalancesOverMinimum);
                }

                catch(Exception ex)
                {
                    await NotifyPayoutFailureAsync(poolBalancesOverMinimum, pool, ex);
                    throw;
                }
            }

            else
                logger.Info(() => $"No balances over configured minimum payout for pool {pool.Id}");
        }

        private Task NotifyPayoutFailureAsync(Balance[] balances, PoolConfig pool, Exception ex)
        {
            messageBus.SendMessage(new PaymentNotification(pool.Id, ex.Message, balances.Sum(x => x.Amount), pool.Template.Symbol));

            return Task.FromResult(true);
        }

        private async Task CalculateBlockEffortAsync(PoolConfig pool, Block block, IPayoutHandler handler)
        {
            // get share date-range
            var from = DateTime.MinValue;
            var to = block.Created;

            // get last block for pool
            var lastBlock = await cf.Run(con => blockRepo.GetBlockBeforeAsync(con, pool.Id, new[]
            {
                BlockStatus.Confirmed,
                BlockStatus.Orphaned,
                BlockStatus.Pending,
            }, block.Created));

            if(lastBlock != null)
                from = lastBlock.Created;

            // get combined diff of all shares for block
            var accumulatedShareDiffForBlock = await cf.Run(con =>
                shareRepo.GetAccumulatedShareDifficultyBetweenCreatedAsync(con, pool.Id, from, to));

            // handler has the final say
            if(accumulatedShareDiffForBlock.HasValue)
                await handler.CalculateBlockEffortAsync(block, accumulatedShareDiffForBlock.Value);
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            try
            {
                // monitor pool lifetime
                disposables.Add(messageBus
                    .Listen<PoolStatusNotification>()
                    .ObserveOn(TaskPoolScheduler.Default)
                    .Subscribe(OnPoolStatusNotification));

                logger.Info(() => "Online");

                // Allow all pools to actually come up before the first payment processing run
                await Task.Delay(TimeSpan.FromMinutes(1), ct);

                while(!ct.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessPoolsAsync();
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
    }
}
