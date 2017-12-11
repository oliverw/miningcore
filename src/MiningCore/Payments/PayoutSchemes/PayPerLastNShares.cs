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
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Payments.PayoutSchemes
{
    /// <summary>
    /// PPLNS payout scheme implementation
    /// </summary>
    public class PayPerLastNShares : IPayoutScheme
    {
        public PayPerLastNShares(IConnectionFactory cf,
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
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private class Config
        {
            public decimal Factor { get; set; }
        }

        #region IPayoutScheme

        public Task UpdateBalancesAsync(IDbConnection con, IDbTransaction tx, PoolConfig poolConfig,
            IPayoutHandler payoutHandler, Block block, decimal blockReward)
        {
            var payoutConfig = poolConfig.PaymentProcessing.PayoutSchemeConfig;

            // PPLNS window (see https://bitcointalk.org/index.php?topic=39832)
            var window = payoutConfig?.ToObject<Config>()?.Factor ?? 2.0m;

            // calculate rewards
            var shares = new Dictionary<string, ulong>();
            var rewards = new Dictionary<string, decimal>();
            var shareCutOffDate = CalculateRewards(poolConfig, window, block, blockReward, shares, rewards);

            // update balances
            foreach(var address in rewards.Keys)
            {
                var amount = rewards[address];

                if (amount > 0)
                {
                    logger.Info(() => $"Adding {payoutHandler.FormatAmount(amount)} to balance of {address} for {shares[address]} shares");
                    balanceRepo.AddAmount(con, tx, poolConfig.Id, poolConfig.Coin.Type, address, amount);
                }
            }

            // delete obsolete shares
            if (shareCutOffDate.HasValue)
            {
                var cutOffCount = shareRepo.CountPoolSharesBefore(con, tx, poolConfig.Id, shareCutOffDate.Value);

                if (cutOffCount > 0)
                {
                    logger.Info(() => $"Deleting {cutOffCount} obsolete shares before {shareCutOffDate.Value}");
                    shareRepo.DeletePoolSharesBefore(con, tx, poolConfig.Id, shareCutOffDate.Value);
                }

                logger.Info(() => $"Shares before {shareCutOffDate.Value} can be deleted");
            }

            // diagnostics
            var totalShareCount = shares.Values.ToList().Sum(x => new decimal(x));
            var totalRewards = rewards.Values.ToList().Sum(x => x);

            if (totalRewards > 0)
                logger.Info(() => $"{totalShareCount} shares contributed to a total payout of {payoutHandler.FormatAmount(totalRewards)} ({totalRewards / blockReward * 100:0.00}% of block reward)");

            return Task.FromResult(true);
        }

        #endregion // IPayoutScheme

        private DateTime? CalculateRewards(PoolConfig poolConfig, decimal window, Block block, decimal blockReward,
            Dictionary<string, ulong> shares, Dictionary<string, decimal> rewards)
        {
            var done = false;
            var pageSize = 10000;
            var currentPage = 0;
            var accumulatedScore = 0.0m;
            var blockRewardRemaining = blockReward;
            DateTime? shareCutOffDate = null;

            while(!done)
            {
                // fetch next page
                var blockPage = cf.Run(con => shareRepo.PageSharesBefore(con, poolConfig.Id, block.Created, currentPage++, pageSize));

                if (blockPage.Length == 0)
                    break;

                // iterate over shares
                var start = Math.Max(0, blockPage.Length - 1);

                for(var i = start; !done && i >= 0; i--)
                {
                    var share = blockPage[i];

                    // build address
                    var address = share.Miner;
                    if (!string.IsNullOrEmpty(share.PayoutInfo))
                        address += PayoutConstants.PayoutInfoSeperator + share.PayoutInfo;

                    // record attributed shares for diagnostic purposes
                    if (!shares.ContainsKey(address))
                        shares[address] = 1;
                    else
                        shares[address] += 1;

                    var score = (decimal) share.Difficulty / (decimal) share.NetworkDifficulty;

                    // if accumulated score would cross threshold, cap it to the remaining value
                    if (accumulatedScore + score >= window)
                    {
                        score = window - accumulatedScore;
                        shareCutOffDate = share.Created;
                        done = true;
                    }

                    // calulate reward
                    var reward = score * blockReward / window;
                    accumulatedScore += score;
                    blockRewardRemaining -= reward;

                    // this should never happen
                    if (blockRewardRemaining <= 0 && !done)
                        throw new OverflowException("blockRewardRemaining < 0");

                    if (reward > 0)
                    {
                        // accumulate miner reward
                        if (!rewards.ContainsKey(address))
                            rewards[address] = reward;
                        else
                            rewards[address] += reward;
                    }
                }
            }

            logger.Info(() => $"Balance-calculation completed with accumulated score {accumulatedScore:0.####} ({(accumulatedScore / window) * 100:0.#}%)");

            return shareCutOffDate;
        }
    }
}
