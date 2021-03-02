/*
Copyright 2017 Coin Foundry (coinfoundry.org)
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
using System.Data.Common;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Util;
using NLog;
using Polly;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Payments.PaymentSchemes
{
    /// <summary>
    /// PROP payout scheme implementation
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public class PROPPaymentScheme : IPayoutScheme
    {
     public PROPPaymentScheme(IConnectionFactory cf,
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

            BuildFaultHandlingPolicy();
        }

        private readonly IBalanceRepository balanceRepo;
        private readonly IBlockRepository blockRepo;
        private readonly IConnectionFactory cf;
        private readonly IShareRepository shareRepo;
        private static readonly ILogger logger = LogManager.GetLogger("PROP Payment", typeof(PROPPaymentScheme));

        private const int RetryCount = 4;
        private IAsyncPolicy shareReadFaultPolicy;

        private class Config
        {
            public decimal Factor { get; set; }
        }

        #region IPayoutScheme

        public async Task UpdateBalancesAsync(IDbConnection con, IDbTransaction tx, PoolConfig poolConfig,
            IPayoutHandler payoutHandler, Block block, decimal blockReward)
        {
            var payoutConfig = poolConfig.PaymentProcessing.PayoutSchemeConfig;
            var shares = new Dictionary<string, double>();
            var rewards = new Dictionary<string, decimal>();
            var shareCutOffDate = await CalculateRewardsAsync(poolConfig, block, blockReward, shares, rewards);

            // update balances
            foreach(var address in rewards.Keys)
            {
                var amount = rewards[address];

                if(amount > 0)
                {
                    logger.Info(() => $"Adding {payoutHandler.FormatAmount(amount)} to balance of {address} for {FormatUtil.FormatQuantity(shares[address])} ({shares[address]}) shares for block {block.BlockHeight}");
                    await balanceRepo.AddAmountAsync(con, tx, poolConfig.Id, address, amount, $"Reward for {FormatUtil.FormatQuantity(shares[address])} shares for block {block.BlockHeight}");
                }
            }

            // delete discarded shares
            if(shareCutOffDate.HasValue)
            {
                var cutOffCount = await shareRepo.CountSharesBeforeCreatedAsync(con, tx, poolConfig.Id, shareCutOffDate.Value);

                if(cutOffCount > 0)
                {
                    await LogDiscardedSharesAsync(poolConfig, block, shareCutOffDate.Value);

                    logger.Info(() => $"Deleting {cutOffCount} discarded shares before {shareCutOffDate.Value:O}");
                    await shareRepo.DeleteSharesBeforeCreatedAsync(con, tx, poolConfig.Id, shareCutOffDate.Value);
                }
            }

            // diagnostics
            var totalShareCount = shares.Values.ToList().Sum(x => new decimal(x));
            var totalRewards = rewards.Values.ToList().Sum(x => x);

            if(totalRewards > 0)
                logger.Info(() => $"{FormatUtil.FormatQuantity((double) totalShareCount)} ({Math.Round(totalShareCount, 2)}) shares contributed to a total payout of {payoutHandler.FormatAmount(totalRewards)} ({totalRewards / blockReward * 100:0.00}% of block reward) to {rewards.Keys.Count} addresses");
        }

        private async Task LogDiscardedSharesAsync(PoolConfig poolConfig, Block block, DateTime value)
        {
            var before = value;
            var pageSize = 100000;
            var currentPage = 0;
            var shares = new Dictionary<string, double>();

            while(true)
            {
                logger.Info(() => $"Fetching page {currentPage} of discarded shares for pool {poolConfig.Id}, block {block.BlockHeight}");

                var page = await shareReadFaultPolicy.ExecuteAsync(() =>
                    cf.Run(con => shareRepo.ReadSharesBeforeCreatedAsync(con, poolConfig.Id, before, false, pageSize)));

                currentPage++;

                for(var i = 0; i < page.Length; i++)
                {
                    var share = page[i];
                    var address = share.Miner;

                    // record attributed shares for diagnostic purposes
                    if(!shares.ContainsKey(address))
                        shares[address] = share.Difficulty;
                    else
                        shares[address] += share.Difficulty;
                }

                if(page.Length < pageSize)
                    break;

                before = page[page.Length - 1].Created;
            }

            if(shares.Keys.Count > 0)
            {
                // sort addresses by shares
                var addressesByShares = shares.Keys.OrderByDescending(x => shares[x]);

                logger.Info(() => $"{FormatUtil.FormatQuantity(shares.Values.Sum())} ({shares.Values.Sum()}) total discarded shares, block {block.BlockHeight}");

                foreach(var address in addressesByShares)
                    logger.Info(() => $"{address} = {FormatUtil.FormatQuantity(shares[address])} ({shares[address]}) discarded shares, block {block.BlockHeight}");
            }
        }

        #endregion // IPayoutScheme

        private async Task<DateTime?> CalculateRewardsAsync(PoolConfig poolConfig, Block block, decimal blockReward,
            Dictionary<string, double> shares, Dictionary<string, decimal> rewards)
        {
            var done = false;
            var before = block.Created;
            var inclusive = true;
            var pageSize = 100000;
            var currentPage = 0;
            var accumulatedScore = 0.0m;
            var blockRewardRemaining = blockReward;
            DateTime? shareCutOffDate = null;
            Dictionary<string, decimal> scores = new Dictionary<string, decimal>();

            while(!done)
            {
                logger.Info(() => $"Fetching page {currentPage} of shares for pool {poolConfig.Id}, block {block.BlockHeight}");

                var page = await shareReadFaultPolicy.ExecuteAsync(() =>
                    cf.Run(con => shareRepo.ReadSharesBeforeCreatedAsync(con, poolConfig.Id, before, inclusive, pageSize)));
                inclusive = false;
                currentPage++;

                for(var i = 0; !done && i < page.Length; i++)
                {
                    var share = page[i];
                    var address = share.Miner;

                    // record attributed shares for diagnostic purposes
                    if(!shares.ContainsKey(address))
                        shares[address] = share.Difficulty;
                    else
                        shares[address] += share.Difficulty;

                    var score = (decimal) (share.Difficulty / share.NetworkDifficulty);

                    if (!scores.ContainsKey(address))
                        scores[address] = score;
                    else
                        scores[address] += score;
                        accumulatedScore += score;

                    // set the cutoff date to clean up old shares after a successful payout
                    if (shareCutOffDate == null || share.Created > shareCutOffDate)

                        shareCutOffDate = share.Created;
                }

                if (page.Length < pageSize)
                {
                    done = true;
                    break;
                }

                before = page[page.Length - 1].Created;
                done = page.Length <= 0;
            }

            if (accumulatedScore > 0)
            {
                var rewardPerScorePoint = blockReward / accumulatedScore;

                // build rewards for all addresses that contributed to the round
                foreach (var address in scores.Select(x => x.Key).Distinct())
                {
                  // loop all scores for the current addres
                  foreach (var score in scores.Where(x => x.Key == address))
                {
                var reward = score.Value * rewardPerScorePoint;
                if (reward > 0)
                {
                  // accumulate miner reward
                  if (!rewards.ContainsKey(address))
                     rewards[address] = reward;
                  else
                     rewards[address] += reward;
                }                            
                  blockRewardRemaining -= reward;
                }
              }
            }
                
            // this should never happen
            if (blockRewardRemaining <= 0 && !done)
                throw new OverflowException("blockRewardRemaining < 0");

            logger.Info(() => $"Balance-calculation for pool {poolConfig.Id}, block {block.BlockHeight} completed with accumulated score {accumulatedScore:0.####} ({accumulatedScore * 100:0.#}%)");

            return shareCutOffDate;
        }

        private void BuildFaultHandlingPolicy()
        {
            var retry = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .RetryAsync(RetryCount, OnPolicyRetry);

            shareReadFaultPolicy = retry;
        }

        private static void OnPolicyRetry(Exception ex, int retry, object context)
        {
            logger.Warn(() => $"Retry {retry} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }
    }
}
