using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using MiningForce.Blockchain;
using MiningForce.MininigPool;
using MiningForce.Persistence.Repositories;
using NLog;
using Polly;

namespace MiningForce.Persistence
{
    public class SharePersister
    {
	    public SharePersister(IShareRepository shares)
	    {
		    this.shares = shares;

		    BuildFaultHandlingPolicy();
	    }

	    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

	    private readonly BlockingCollection<IShare> queue = new BlockingCollection<IShare>();
		private readonly IShareRepository shares;

		private const int RetryCount = 8;
	    private Policy faultPolicy;

	    #region API-Surface

		public void AttachPool(Pool pool)
	    {
		    pool.Shares.Subscribe(share => queue.Add(share));
	    }

	    public void Start()
	    {
		    var thread = new Thread(() =>
		    {
			    logger.Info(() => "Share persistence queue online");
			    var isCleanExit = false;

				while (true)
			    {
				    try
				    {
					    var share = queue.Take();

						PersistShare(share);
				    }

				    catch (ObjectDisposedException)
				    {
						// queue has been disposed
					    isCleanExit = true;
					    break;
				    }

					catch (Exception ex)
				    {
					    logger.Error(ex);
				    }
			    }

			    if (!isCleanExit)
			    {
				    logger.Fatal(() => "Share persistence queue thread exiting prematurely! Restarting ...");
				    Start();
			    }
		    });

		    thread.IsBackground = false;
			thread.Priority = ThreadPriority.AboveNormal;
		    thread.Name = "Share Persistence Queue";
		    thread.Start();
	    }

	    private void BuildFaultHandlingPolicy()
	    {
		    // using the policy below, the 8th retry will wait 4min26sec (would be 17min with 10 retries)
		    var retry = Policy
			    .Handle<DbException>()
			    .Or<TimeoutException>()
			    .Or<OutOfMemoryException>()
			    .WaitAndRetry(RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), OnRetry);

			// TODO: add fallback for writing json file in case of database failure
			// requires polly 5.2.1 which allows to pass context to fallback action
		    //var fallback = Policy
			   // .Handle<DbException>()
			   // .Or<TimeoutException>()
			   // .Or<OutOfMemoryException>()
			   // .Fallback(x => { });

		    //Policy.Wrap(fallback, retry).Execute(() =>
		    //{
			   // return 2;
		    //});

		    faultPolicy = retry;
	    }

		#endregion // API-Surface

		private void PersistShare(IShare share)
	    {
		    var context = new Dictionary<string, object> {{"share", share}};

			faultPolicy.Execute(() =>
		    {
				shares.PutShare(share);
		    }, context);
		}

	    private static void OnRetry(Exception ex, TimeSpan timeSpan, int retry, object context)
	    {
		    logger.Warn(()=> $"Retry {1} in {timeSpan} due to: {ex}");
	    }
	}
}
