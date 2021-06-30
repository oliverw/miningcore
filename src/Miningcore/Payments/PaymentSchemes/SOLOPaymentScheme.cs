using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Miningcore.Configuration;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using NLog;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Payments.PaymentSchemes
{
    /// <summary>
    /// SOLO payout scheme implementation
    /// </summary>
    /// ReSharper disable once InconsistentNaming
    public class SOLOPaymentScheme : IPayoutScheme
    {
        public SOLOPaymentScheme(
            IShareRepository shareRepo,
            IBalanceRepository balanceRepo)
        {
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));

            this.shareRepo = shareRepo;
            this.balanceRepo = balanceRepo;
        }

        private readonly IBalanceRepository balanceRepo;
        private readonly IShareRepository shareRepo;
        private static readonly ILogger logger = LogManager.GetLogger("SOLO Payment", typeof(SOLOPaymentScheme));

        #region IPayoutScheme

        public async Task UpdateBalancesAsync(IDbConnection con, IDbTransaction tx, PoolConfig poolConfig, IPayoutHandler payoutHandler, Block block, decimal blockReward)
        {
            // calculate rewards
            var rewards = new Dictionary<string, decimal>();
            var shareCutOffDate = CalculateRewards(block, blockReward, rewards);

            // update balances
            foreach(var address in rewards.Keys)
            {
                var amount = rewards[address];

                if(amount > 0)
                {
                    logger.Info(() => $"Adding {payoutHandler.FormatAmount(amount)} to balance of {address} for block {block.BlockHeight}");

                    await balanceRepo.AddAmountAsync(con, tx, poolConfig.Id, address, amount, $"Reward for block {block.BlockHeight}");
                }
            }

            // delete discarded shares
            if(shareCutOffDate.HasValue)
            {
                var cutOffCount = await shareRepo.CountSharesByMinerAsync(con, tx, poolConfig.Id, block.Miner);

                if(cutOffCount > 0)
                {
#if !DEBUG
                    logger.Info(() => $"Deleting {cutOffCount} discarded shares for {block.Miner}");

                    await shareRepo.DeleteSharesByMinerAsync(con, tx, poolConfig.Id, block.Miner);
#endif
                }
            }
        }

        #endregion // IPayoutScheme

        private DateTime? CalculateRewards(Block block, decimal blockReward, Dictionary<string, decimal> rewards)
        {
            rewards[block.Miner] = blockReward;

            return block.Created;
        }
    }
}
