using System;
using System.Threading;
using Autofac;
using NLog;

namespace MiningForce.Payments
{
	/// <summary>
	/// Coin agnostic payment processor
	/// </summary>
    public class PaymentProcessor
    {
	    public PaymentProcessor(IComponentContext ctx)
	    {
		    this.ctx = ctx;
	    }

		private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
	    private readonly IComponentContext ctx;

	    #region API-Surface

		public void Start()
	    {
		    var thread = new Thread(() =>
		    {
			    logger.Info(() => "Online");

			    while (true)
			    {
				    try
				    {
				    }

				    catch (Exception ex)
				    {
					    logger.Error(ex);
				    }
			    }
		    });

		    thread.IsBackground = false;
		    thread.Priority = ThreadPriority.AboveNormal;
		    thread.Name = "Payment Processor";
		    thread.Start();
	    }
	
		#endregion // API-Surface
    }
}
