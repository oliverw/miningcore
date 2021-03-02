/*
Copyright 2017 - 2021 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)
         Olaf Wasilewski (olaf.wasilewski@gmx.de)

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
        public SOLOPaymentScheme(IConnectionFactory cf,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo)
        {
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));

            this.cf = cf;
            this.shareRepo = shareRepo;
            this.blockRepo = blockRepo;
            this.balanceRepo = balanceRepo;
        }

        private readonly IBalanceRepository balanceRepo;
        private readonly IBlockRepository blockRepo;
        private readonly IConnectionFactory cf;
        private readonly IShareRepository shareRepo;
        private static readonly ILogger logger = LogManager.GetLogger("SOLO Payment", typeof(SOLOPaymentScheme));

        private class Config
        {
            public decimal Factor { get; set; }
        }

        #region IPayoutScheme

        public async Task UpdateBalancesAsync(IDbConnection con, IDbTransaction tx, PoolConfig poolConfig, IPayoutHandler payoutHandler, Block block, decimal blockReward)
        {
            // calculate rewards
            var rewards = new Dictionary<string, decimal>();
            var shareCutOffDate = CalculateRewards(poolConfig, block, blockReward, rewards);

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
                var cutOffCount = await shareRepo.CountSharesSoloBeforeCreatedAsync(con, tx, poolConfig.Id, block.Miner, shareCutOffDate.Value);

                if(cutOffCount > 0)
                {
#if !DEBUG
                    logger.Info(() => $"Deleting {cutOffCount} discarded shares for {block.Miner}");
                    await shareRepo.DeleteSharesSoloBeforeCreatedAsync(con, tx, poolConfig.Id, block.Miner, shareCutOffDate.Value);
#endif
                }
            }
        }

        #endregion // IPayoutScheme

        private DateTime? CalculateRewards(PoolConfig poolConfig, Block block, decimal blockReward, Dictionary<string, decimal> rewards)
        {
            rewards[block.Miner] = blockReward;
            return block.Created;
        }
    }
}
