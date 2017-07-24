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
			    blockRepo.UpdateBlock(con, tx, block);
		    });
		}

		#endregion // IPayoutScheme

	    private Dictionary<string, double> CalculateBalances(double d, double x, Persistence.Model.Block block)
	    {
		    var result = new Dictionary<string, double>();
		    var pageSize = 1000;
		    var currentPage = 0;
		    var targetScore = d * x;
		    var totalScore = 0.0;

		    while (true)
		    {
				// fetch next page
			    var page = shareRepo.PageSharesBefore(block.Created, currentPage, pageSize);

				// done if no more shares
				if (page.Length == 0)
				    break;

				// iterate over shares (skip winning share)
			    var start = Math.Max(0, page.Length - 1);

				for (int i = start; i > 0; i--)
			    {
				    var share = page[i];

				    var score = 1.0 / share.NetworkDifficulty;
			    }
		    }

		    return result;
	    }
	}
}
