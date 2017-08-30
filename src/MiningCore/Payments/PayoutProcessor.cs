using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Features.Metadata;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Payments
{
    /// <summary>
    /// Coin agnostic payment processor
    /// </summary>
    public class PayoutProcessor
    {
        public PayoutProcessor(IComponentContext ctx,
            IConnectionFactory cf,
            IBlockRepository blockRepo,
            IShareRepository shareRepo,
            IBalanceRepository balanceRepo)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));

            this.ctx = ctx;
            this.cf = cf;
            this.blockRepo = blockRepo;
            this.shareRepo = shareRepo;
            this.balanceRepo = balanceRepo;
        }

        private readonly IBalanceRepository balanceRepo;
        private readonly IBlockRepository blockRepo;
        private readonly IConnectionFactory cf;
        private readonly IComponentContext ctx;
        private readonly IShareRepository shareRepo;
        private readonly AutoResetEvent stopEvent = new AutoResetEvent(false);
        private ClusterConfig clusterConfig;
        private Thread thread;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private async Task ProcessPoolsAsync()
        {
            foreach (var pool in clusterConfig.Pools)
            {
                logger.Info(() => $"Processing payments for pool {pool.Id}");

                try
                {
                    // resolve payout handler
                    var handlerImpl = ctx.Resolve<IEnumerable<Meta<Lazy<IPayoutHandler, CoinMetadataAttribute>>>>()
                        .First(x => x.Value.Metadata.SupportedCoins.Contains(pool.Coin.Type)).Value;

                    var handler = handlerImpl.Value;
                    handler.Configure(clusterConfig, pool);

                    // resolve payout scheme
                    var scheme = ctx.ResolveKeyed<IPayoutScheme>(pool.PaymentProcessing.PayoutScheme);

//GenerateTestShares(pool.Id);
                    await UpdatePoolBalancesAsync(pool, handler, scheme);
                    await PayoutPoolBalancesAsync(pool, handler);
                }

                catch (Exception ex)
                {
                    logger.Error(ex, () => $"[{pool.Id}] Payment processing failed");
                }
            }
        }

        private async Task UpdatePoolBalancesAsync(PoolConfig pool, IPayoutHandler handler, IPayoutScheme scheme)
        {
            // get pending blockRepo for pool
            var pendingBlocks = cf.Run(con => blockRepo.GetPendingBlocksForPool(con, pool.Id));

            // classify
            var updatedBlocks = await handler.ClassifyBlocksAsync(pendingBlocks);

            foreach (var block in updatedBlocks.OrderBy(x => x.Created))
            {
                logger.Info(() => $"Processing payments for pool {pool.Id}, block {block.Blockheight}");

                await cf.RunTxAsync(async (con, tx) =>
                {
                    if (block.Status == BlockStatus.Confirmed)
                    {
                        // blockchains that do not support sending out block-rewards using custom coinbase tx
                        // must generate balance records for all reward recipients instead
                        await handler.UpdateBlockRewardBalancesAsync(con, tx, block, pool);

                        // update share submitter balances through configured payout scheme 
                        await scheme.UpdateBalancesAsync(con, tx, pool, handler, block);

                        // finally update block status
                        blockRepo.UpdateBlock(con, tx, block);
                    }

                    else if (block.Status == BlockStatus.Orphaned)
                    {
                        blockRepo.DeleteBlock(con, tx, block);
                    }
                });
            }
        }

        private async Task PayoutPoolBalancesAsync(PoolConfig pool, IPayoutHandler handler)
        {
            var poolBalancesOverMinimum = cf.Run(con =>
                balanceRepo.GetPoolBalancesOverThreshold(con, pool.Id, pool.PaymentProcessing.MinimumPayment));

            if (poolBalancesOverMinimum.Length > 0)
                await handler.PayoutAsync(poolBalancesOverMinimum);
            else
                logger.Info(() => $"No balances over configured minimum payout for pool {pool.Id}");
        }

        private void GenerateTestShares(string poolid)
        {
#if DEBUG
            var numShares = 10000;
            var shareOffset = TimeSpan.FromSeconds(10);

            cf.RunTx((con, tx) =>
            {
                var block = new Block
                {
                    Created = DateTime.UtcNow,
                    Id = 4,
                    Blockheight = 334324,
                    PoolId = "btc1",
                    Status = BlockStatus.Pending,
                    TransactionConfirmationData = "foobar"
                };

                blockRepo.Insert(con, tx, block);

                var shareDate = block.Created;

                for (var i = 0; i < numShares; i++)
                {
                    var share = new Share
                    {
                        Difficulty = (i & 1) == 0 ? 16 : 32,
                        NetworkDifficulty = 236000,
                        Blockheight = block.Blockheight,
                        IpAddress = "127.0.0.1",
                        Created = shareDate,
                        Miner = (i & 1) == 0
                            ? "mkeiTodVRTseFymDbgi2HAV3Re8zv3DQFf"
                            : "n37zNp1QbtwHh9jVUThe6ZgCxvm9rdpX2f",
                        PoolId = poolid
                    };

                    shareDate -= shareOffset;

                    shareRepo.Insert(con, tx, share);
                }
            });
#endif
        }

        #region API-Surface

        public void Configure(ClusterConfig clusterConfig)
        {
            this.clusterConfig = clusterConfig;
        }

        public void Start()
        {
            thread = new Thread(async () =>
            {
                logger.Info(() => "Online");

                var interval = TimeSpan.FromSeconds(
                    clusterConfig.PaymentProcessing.Interval > 0 ? clusterConfig.PaymentProcessing.Interval : 600);

                while (true)
                {
                    try
                    {
                        await ProcessPoolsAsync();
                    }

                    catch (Exception ex)
                    {
                        logger.Error(ex);
                    }

                    var waitResult = stopEvent.WaitOne(interval);

                    // check if stop was signalled
                    if (waitResult)
                        break;
                }
            });

            thread.IsBackground = true;
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Name = "Payment Processing";
            thread.Start();
        }

        public void Stop()
        {
            logger.Info(() => "Stopping ..");

            stopEvent.Set();
            thread.Join();

            logger.Info(() => "Stopped");
        }

        #endregion // API-Surface
    }
}
