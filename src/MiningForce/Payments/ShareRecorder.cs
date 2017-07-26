using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using AutoMapper;
using CodeContracts;
using MiningForce.Blockchain;
using MiningForce.Extensions;
using MiningForce.MininigPool;
using MiningForce.Persistence;
using MiningForce.Persistence.Repositories;
using NLog;
using Polly;

namespace MiningForce.Payments
{
	/// <summary>
	/// Asynchronously persist shares produced by all pools for processing by coin-specific payment processor(s)
	/// </summary>
    public class ShareRecorder
	{
		public ShareRecorder(IConnectionFactory cf, IMapper mapper,
			IShareRepository shareRepo, IBlockRepository blockRepo)
	    {
		    Contract.RequiresNonNull(cf, nameof(cf));
		    Contract.RequiresNonNull(mapper, nameof(mapper));
		    Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
		    Contract.RequiresNonNull(blockRepo, nameof(blockRepo));

			this.cf = cf;
		    this.mapper = mapper;

			this.shareRepo = shareRepo;
		    this.blockRepo = blockRepo;

			BuildFaultHandlingPolicy();
	    }

	    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

	    private readonly IConnectionFactory cf;
	    private readonly IMapper mapper;
	    private readonly BlockingCollection<IShare> queue = new BlockingCollection<IShare>();
		private readonly IShareRepository shareRepo;
	    private readonly IBlockRepository blockRepo;

		private const int RetryCount = 8;
	    private Policy faultPolicy;

		private int QueueSizeWarningThreshold = 1024;

	    #region API-Surface

		public void AttachPool(Pool pool)
	    {
		    pool.Shares.Subscribe(share =>
		    {
			    queue.Add(share);
		    });
	    }

	    public void Start()
	    {
		    var thread = new Thread(() =>
		    {
			    logger.Info(() => "Online");

				var isCleanExit = false;
			    var hasWarnedAboutBacklogSize = false;

				while (true)
			    {
				    try
				    {
					    var share = queue.Take();

						PersistShareFaulTolerant(share);

					    CheckQueueBacklog(ref hasWarnedAboutBacklogSize);
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

	    private void CheckQueueBacklog(ref bool hasWarnedAboutBacklogSize)
	    {
		    if (queue.Count > QueueSizeWarningThreshold)
		    {
			    if (!hasWarnedAboutBacklogSize)
			    {
				    logger.Warn(() => $"Share persistence queue backlog has crossed {QueueSizeWarningThreshold}");
				    hasWarnedAboutBacklogSize = true;
			    }
		    }

		    else if (hasWarnedAboutBacklogSize && queue.Count <= QueueSizeWarningThreshold / 2)
			    hasWarnedAboutBacklogSize = false;
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

		private void PersistShareFaulTolerant(IShare share)
	    {
		    var context = new Dictionary<string, object> {{"share", share}};

			faultPolicy.Execute(() =>
			{
				PersistShare(share);
			}, context);
		}

	    private static void OnRetry(Exception ex, TimeSpan timeSpan, int retry, object context)
	    {
		    logger.Warn(()=> $"Retry {1} in {timeSpan} due to: {ex}");
	    }

	    private void PersistShare(IShare share)
	    {
		    cf.RunTx((con, tx) =>
		    {
			    var shareEntity = mapper.Map<Persistence.Model.Share>(share);
			    shareRepo.Insert(con, tx, shareEntity);

			    if (share.IsBlockCandidate)
			    {
				    var blockEntity = mapper.Map<Persistence.Model.Block>(share);
					blockEntity.Status = Persistence.Model.BlockStatus.Pending;
				    blockRepo.Insert(con, tx, blockEntity);
			    }
		    });
	    }
	}
}
