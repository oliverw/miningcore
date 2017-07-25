using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using MiningForce.Extensions;
using MiningForce.Persistence;
using MiningForce.Persistence.Repositories;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace MiningForce.Payments.PayoutSchemes
{
    public class PayPerLastNShares : IPayoutScheme
    {
	    public PayPerLastNShares(IConnectionFactory cf, 
			IShareRepository shareRepo, IBlockRepository blockRepo)
	    {
		    Contract.RequiresNonNull(cf, nameof(cf));
		    Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
		    Contract.RequiresNonNull(blockRepo, nameof(blockRepo));

			this.cf = cf;
		    this.shareRepo = shareRepo;
		    this.blockRepo = blockRepo;
	    }

		private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
	    private readonly IConnectionFactory cf;
	    private readonly IShareRepository shareRepo;
	    private readonly IBlockRepository blockRepo;

		private class Config
	    {
		    public double Factor { get; set; }
	    }

	    #region IPayoutScheme

	    public async Task UpdateBalancesAndBlockAsync(JToken payoutConfig, 
			IPayoutHandler payoutHandler, Persistence.Model.Block block)
	    {
			// PPLNS Window multiplier
		    var factor = payoutConfig?.ToObject<Config>()?.Factor ?? 2.0;

			// Query current difficulty
		    var networkDifficulty = await payoutHandler.GetNetworkDifficultyAsync();

			// holds pending balances per address (in our case workername = address)
			var balances = CalculateBalances(networkDifficulty, factor, block);

		    cf.RunTx((con, tx) =>
		    {
			    // finally update block status
			    //blockRepo.UpdateBlock(con, tx, block);
		    });
		}

		#endregion // IPayoutScheme

	    private Dictionary<string, double> CalculateBalances(double networkDifficulty, double factor, Persistence.Model.Block block)
	    {
			var result = new Dictionary<string, double>();	// maps address to balance
		    var done = false;
		    var pageSize = 10000;
		    var currentPage = 0;
		    var targetScore = networkDifficulty * factor;
		    var accumulatedScore = 0.0;
		    var blockReward = block.Reward.Value;
			var blockRewardRemaining = blockReward;

			while (!done)
			{
				// fetch next page
				var blockPage = cf.Run(con => shareRepo.PageSharesBefore(con, block.Created, currentPage++, pageSize));

				// done if no more shares
				if (blockPage.Length == 0)
					break;

				// iterate over shares
				var start = Math.Max(0, blockPage.Length - 1);

				for (var i = start; i >= 0; i--)
				{
					var share = blockPage[i];

					var score = share.Difficulty / share.NetworkDifficulty;

					// if accumulated score would cross threshold, cap it to the remaining value
					if (accumulatedScore + score >= targetScore)
					{
						score = targetScore - accumulatedScore;
						done = true;
					}

					var reward = score * blockReward;
					accumulatedScore += score;
					blockRewardRemaining -= reward;

					// accumulate per-worker reward
					if (!result.ContainsKey(share.Worker))
						result[share.Worker] = reward;
					else
						result[share.Worker] += reward;
				}
			}

			return result;
	    }
	}
}
