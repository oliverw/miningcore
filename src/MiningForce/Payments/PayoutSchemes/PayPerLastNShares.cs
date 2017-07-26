using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeContracts;
using MiningForce.Configuration;
using MiningForce.Extensions;
using MiningForce.Persistence;
using MiningForce.Persistence.Model;
using MiningForce.Persistence.Repositories;
using NLog;

namespace MiningForce.Payments.PayoutSchemes
{
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

		private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
	    private readonly IConnectionFactory cf;
	    private readonly IShareRepository shareRepo;
	    private readonly IBlockRepository blockRepo;
	    private readonly IBalanceRepository balanceRepo;

	    private class Config
	    {
		    public decimal Factor { get; set; }
	    }

	    #region IPayoutScheme

	    public Task UpdateBalancesAndBlockAsync(PoolConfig poolConfig, IPayoutHandler payoutHandler, Block block)
	    {
		    var payoutConfig = poolConfig.PaymentProcessing.PayoutSchemeConfig;

			// PPLNS window (see https://bitcointalk.org/index.php?topic=39832)
			var factorX = payoutConfig?.ToObject<Config>()?.Factor ?? 2.0m;

			// holds pending balances per address (in our case workername = address)
		    var payouts = new Dictionary<string, decimal>();
			var shareCutOffDate = CalculatePayouts(poolConfig, factorX, block, payouts);

		    cf.RunTx((con, tx) =>
		    {
				// update balances
			    foreach (var address in payouts.Keys)
			    {
				    var amount = payouts[address];

					logger.Info(() => $"Adding {payoutHandler.FormatRewardAmount(amount)} to balance of {address}");
					balanceRepo.AddAmount(con, tx, poolConfig.Id, poolConfig.Coin.Type, address, amount);
			    }

			    // delete obsolete shares
			    if (shareCutOffDate.HasValue)
			    {
					var cutOffCount = shareRepo.CountSharesBefore(con, tx, poolConfig.Id, shareCutOffDate.Value);

				    if (cutOffCount > 0)
				    {
					    logger.Info(() => $"Deleting {cutOffCount} obsolete shares before {shareCutOffDate.Value}");
					    shareRepo.DeleteSharesBefore(con, tx, poolConfig.Id, shareCutOffDate.Value);
				    }
			    }

			    // finally update block status
			    blockRepo.UpdateBlock(con, tx, block);
		    });

		    return Task.FromResult(true);
	    }

		#endregion // IPayoutScheme

	    private DateTime? CalculatePayouts(PoolConfig poolConfig, decimal factorX, Block block, Dictionary<string, decimal> payouts)
	    {
			var done = false;
		    var pageSize = 10000;
		    var currentPage = 0;
		    var accumulatedScore = 0.0m;
		    var blockReward = block.Reward;
			var blockRewardRemaining = blockReward;
		    DateTime? shareCutOffDate = null;

			while (!done)
			{
				// fetch next page
				var blockPage = cf.Run(con => shareRepo.PageSharesBefore(con, poolConfig.Id, block.Created, currentPage++, pageSize));

				// done if no more shares
				if (blockPage.Length == 0)
					break;

				// iterate over shares
				var start = Math.Max(0, blockPage.Length - 1);

				for (var i = start; i >= 0; i--)
				{
					var share = blockPage[i];
					var score = (decimal) (share.Difficulty / share.NetworkDifficulty);

					// if accumulated score would cross threshold, cap it to the remaining value
					if (accumulatedScore + score >= factorX)
					{
						score = factorX - accumulatedScore;
						shareCutOffDate = share.Created;
						done = true;
					}

					// calulate reward
					var reward = (score * blockReward) / factorX;
					accumulatedScore += score;
					blockRewardRemaining -= reward;

					// this should never happen
					if(blockRewardRemaining <= 0)
						throw new OverflowException("blockRewardRemaining < 0");

					// accumulate per-worker reward
					var address = share.Worker.Trim();

					if (!payouts.ContainsKey(address))
						payouts[address] = reward;
					else
						payouts[address] += reward;
				}
			}

			return shareCutOffDate;
	    }
	}
}
