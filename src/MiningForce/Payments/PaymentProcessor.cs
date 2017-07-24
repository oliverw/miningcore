using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using MiningForce.Configuration;
using MiningForce.Extensions;
using MiningForce.Persistence;
using MiningForce.Persistence.Repositories;
using NLog;

namespace MiningForce.Payments
{
	/// <summary>
	/// Coin agnostic payment processor
	/// </summary>
    public class PaymentProcessor
    {
	    public PaymentProcessor(IComponentContext ctx, 
			IConnectionFactory cf, IBlockRepository blocks)
	    {
		    this.ctx = ctx;
		    this.cf = cf;
		    this.blocks = blocks;
	    }

		private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
	    private readonly IComponentContext ctx;
	    private readonly IConnectionFactory cf;
	    private readonly IBlockRepository blocks;
	    private ClusterConfig clusterConfig;
	    private Dictionary<CoinType, IPayoutHandler> payoutHandlers;

		#region API-Surface

		public void Configure(ClusterConfig clusterConfig)
		{
			this.clusterConfig = clusterConfig;
		}

		public void Start()
	    {
		    ResolveHandlers();

			var thread = new Thread(async () =>
		    {
			    logger.Info(() => "Online");

			    var interval = TimeSpan.FromSeconds(
					clusterConfig.PaymentProcessing.Interval > 0 ? clusterConfig.PaymentProcessing.Interval : 600);

				while (true)
			    {
				    var now = DateTime.UtcNow;

					try
					{
						await ProcessPoolsAsync();
				    }

				    catch (Exception ex)
				    {
					    logger.Error(ex);
				    }

				    var elapsed = DateTime.UtcNow - now;
				    var remaining = interval - elapsed;

				    if (remaining.TotalSeconds > 0)
					    Thread.Sleep(remaining);
				}
			});

		    thread.IsBackground = false;
		    thread.Priority = ThreadPriority.AboveNormal;
		    thread.Name = "Payment Processor";
		    thread.Start();
	    }

		#endregion // API-Surface

		private void ResolveHandlers()
	    {
		    payoutHandlers = clusterConfig.Pools.ToDictionary(x => x.Coin.Type, x =>
		    {
			    var handler = ctx.ResolveKeyed<IPayoutHandler>(x.Coin.Type);
			    handler.Configure(x);
			    return handler;
		    });
	    }

		private async Task ProcessPoolsAsync()
	    {
			logger.Info(()=> "Processing payments");

		    foreach (var pool in clusterConfig.Pools)
		    {
			    try
			    {
				    var handler = payoutHandlers[pool.Coin.Type];

				    // get pending blocks for pool
				    var pendingBlocks = await handler.GetConfirmedPendingBlocksAsync();

				    // get confirmation status for each block
			    }

				catch (Exception ex)
			    {
					logger.Error(ex, ()=> $"[{pool.Id}] Payment processing failed");
			    }
		    }
		}
    }
}
