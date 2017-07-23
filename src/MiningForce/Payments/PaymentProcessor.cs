using System;
using System.Threading;
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
			IConnectionFactory cf, IBlockRepository blocks,
			ClusterConfig clusterConfig)
	    {
		    this.ctx = ctx;
		    this.cf = cf;
		    this.blocks = blocks;
			this.clusterConfig = clusterConfig;
	    }

		private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
	    private readonly IComponentContext ctx;
	    private readonly IConnectionFactory cf;
	    private readonly IBlockRepository blocks;
	    private readonly ClusterConfig clusterConfig;

	    #region API-Surface

		public void Start()
	    {
		    var thread = new Thread(() =>
		    {
			    logger.Info(() => "Online");

			    var interval = TimeSpan.FromSeconds(
					clusterConfig.PaymentProcessing.Interval > 0 ? clusterConfig.PaymentProcessing.Interval : 600);

				while (true)
			    {
				    var now = DateTime.UtcNow;

					try
					{
						ProcessCoins();
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

	    private void ProcessCoins()
	    {
			logger.Info(()=> "Processing payments");

		    foreach (var pool in clusterConfig.Pools)
		    {
				// get pending blocks for pool
			    var pendingBlocks = cf.Run(con => blocks.GetPendingBlocksForPool(con, pool.Id));

				// get confirmation status for each block
		    }
		}
    }
}
