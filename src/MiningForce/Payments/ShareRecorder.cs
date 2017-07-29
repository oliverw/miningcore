using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using AutoMapper;
using CodeContracts;
using MiningForce.Blockchain;
using MiningForce.Configuration;
using MiningForce.Extensions;
using MiningForce.MininigPool;
using MiningForce.Persistence;
using MiningForce.Persistence.Model;
using MiningForce.Persistence.Repositories;
using Newtonsoft.Json;
using NLog;
using Polly;
using Polly.CircuitBreaker;

namespace MiningForce.Payments
{
	/// <summary>
	/// Asynchronously persist shares produced by all pools for processing by coin-specific payment processor(s)
	/// </summary>
    public class ShareRecorder
	{
		public ShareRecorder(IConnectionFactory cf, IMapper mapper,
			JsonSerializerSettings jsonSerializerSettings,
			IShareRepository shareRepo, IBlockRepository blockRepo)
	    {
		    Contract.RequiresNonNull(cf, nameof(cf));
		    Contract.RequiresNonNull(mapper, nameof(mapper));
		    Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
		    Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
		    Contract.RequiresNonNull(jsonSerializerSettings, nameof(jsonSerializerSettings));

			this.cf = cf;
		    this.mapper = mapper;
		    this.jsonSerializerSettings = jsonSerializerSettings;

			this.shareRepo = shareRepo;
		    this.blockRepo = blockRepo;

			BuildFaultHandlingPolicy();
	    }

	    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

	    private readonly IConnectionFactory cf;
	    private readonly IMapper mapper;
		private readonly JsonSerializerSettings jsonSerializerSettings;
	    private readonly BlockingCollection<IShare> queue = new BlockingCollection<IShare>();
		private readonly IShareRepository shareRepo;
	    private readonly IBlockRepository blockRepo;

		private const int RetryCount = 3;
		private const string PolicyContextKeyShare = "share";
		private Policy faultPolicy;
		private bool hasLoggedPolicyFallbackFailure = false;
		private string recoveryFilename;

		private int QueueSizeWarningThreshold = 1024;
		
		#region API-Surface

		public void AttachPool(Pool pool)
	    {
		    pool.Shares.Subscribe(share =>
		    {
			    queue.Add(share);
		    });
	    }

	    public void Start(ClusterConfig clusterConfig)
	    {
		    recoveryFilename = !string.IsNullOrEmpty(clusterConfig.PaymentProcessing?.ShareRecoveryFile)
			    ? clusterConfig.PaymentProcessing.ShareRecoveryFile
			    : "recovered-shares.json";

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
				    Start(clusterConfig);
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
		    var retry = Policy
			    .Handle<DbException>()
			    .Or<SocketException>()
				.Or<TimeoutException>()
			    .WaitAndRetry(RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), OnPolicyRetry);

		    var breaker = Policy
			    .Handle<DbException>()
			    .Or<SocketException>()
			    .Or<TimeoutException>()
			    .CircuitBreaker(exceptionsAllowedBeforeBreaking: 2, durationOfBreak: TimeSpan.FromMinutes(1));

			var fallback = Policy
			    .Handle<DbException>()
			    .Or<SocketException>()
			    .Or<TimeoutException>()
			    .Fallback(OnExecutePolicyFallback, OnPolicyFallback);

		    var fallbackOnBrokenCircuit = Policy
			    .Handle<BrokenCircuitException>()
			    .Fallback(OnExecutePolicyFallback, (Exception ex, Context context)=> {});

			faultPolicy = Policy.Wrap(
				fallbackOnBrokenCircuit, 
				Policy.Wrap(fallback, breaker, retry));
	    }

		#endregion // API-Surface

		private void PersistShareFaulTolerant(IShare share)
	    {
		    var context = new Dictionary<string, object> {{PolicyContextKeyShare, share}};

			faultPolicy.Execute(() =>
			{
				PersistShare(share);
			}, context);
		}

		private void PersistShare(IShare share)
		{
			cf.RunTx((con, tx) =>
			{
				var shareEntity = mapper.Map<Share>(share);
				shareRepo.Insert(con, tx, shareEntity);

				if (share.IsBlockCandidate)
				{
					var blockEntity = mapper.Map<Block>(share);
					blockEntity.Status = BlockStatus.Pending;
					blockRepo.Insert(con, tx, blockEntity);
				}
			});
		}

		private static void OnPolicyRetry(Exception ex, TimeSpan timeSpan, int retry, object context)
	    {
		    logger.Warn(()=> $"Retry {retry} in {timeSpan} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
	    }

		private void OnPolicyFallback(Exception ex, Context context)
		{
			logger.Warn(() => $"Fallback due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
		}

		private void OnExecutePolicyFallback(Context context)
		{
			var share = (IShare) context[PolicyContextKeyShare];

			try
			{
				using (var stream = new FileStream(recoveryFilename, FileMode.Append, FileAccess.Write))
				{
					using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
					{
						if (stream.Length == 0)
							WriteRecoveryFileheader(writer);

						var json = JsonConvert.SerializeObject(share, jsonSerializerSettings);
						writer.WriteLine(json);
					}
				}
			}

			catch (Exception ex)
			{
				if (!hasLoggedPolicyFallbackFailure)
				{
					logger.Fatal(ex, "Fatal error during policy fallback execution. Share(s) will be lost!");
					hasLoggedPolicyFallbackFailure = true;
				}
			}
		}

		private static void WriteRecoveryFileheader(StreamWriter writer)
		{
			writer.WriteLine("// The existence of this file means shares could not be committed to the database.");
			writer.WriteLine("// You should stop the pool cluster and run the following command:");
			writer.WriteLine("// miningforce -c <path-to-config> -rs <path-to-this-file>\n");
		}

		public void RecoverShares(ClusterConfig clusterConfig, string recoveryFilename)
		{
			logger.Info(() => $"Recovering shares using {recoveryFilename} ...");

			try
			{
				int successCount = 0;
				int failCount = 0;

				using (var stream = new FileStream(recoveryFilename, FileMode.Open, FileAccess.Read))
				{
					using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
					{
						while (!reader.EndOfStream)
						{
							var line = reader.ReadLine().Trim();

							// skip blank lines
							if (line.Length == 0)
								continue;

							// skip comments
							if (line.StartsWith("//"))
								continue;

							try
							{
								var share = JsonConvert.DeserializeObject<ShareBase>(line, jsonSerializerSettings);
								PersistShare(share);
								successCount++;
							}

							catch (Exception ex)
							{
								logger.Error(ex, ()=> $"Unable to import share record: {line}");
								failCount++;
							}
						}
					}
				}

				if(failCount == 0)
					logger.Info(() => $"Successfully recovered {successCount} shares");
				else
					logger.Warn(() => $"Successfully {successCount} shares with {failCount} failures");
			}

			catch (FileNotFoundException)
			{
				logger.Error(()=> $"Recovery file {recoveryFilename} was not found");
			}
		}
	}
}
