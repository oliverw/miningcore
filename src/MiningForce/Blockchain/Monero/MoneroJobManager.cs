using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using MiningForce.Blockchain.Bitcoin;
using MiningForce.Blockchain.Monero.DaemonRequests;
using MiningForce.Blockchain.Monero.DaemonResponses;
using MiningForce.Daemon;
using MiningForce.Extensions;
using MiningForce.Stratum;
using MiningForce.Util;
using Newtonsoft.Json.Linq;
using MDC = MiningForce.Blockchain.Monero.MoneroDaemonCommands;

namespace MiningForce.Blockchain.Monero
{
    public class MoneroJobManager : JobManagerBase<MoneroWorkerContext, MoneroJob>,
        IBlockchainJobManager
    {
        public MoneroJobManager(
            IComponentContext ctx, 
            DaemonClient daemon,
            ExtraNonceProvider extraNonceProvider) : 
			base(ctx, daemon)
        {
	        Contract.RequiresNonNull(ctx, nameof(ctx));
	        Contract.RequiresNonNull(daemon, nameof(daemon));
	        Contract.RequiresNonNull(extraNonceProvider, nameof(extraNonceProvider));

			this.extraNonceProvider = extraNonceProvider;

	        daemon.RpcUrl = "json_rpc";
        }

        private readonly ExtraNonceProvider extraNonceProvider;
        private readonly NetworkStats networkStats = new NetworkStats();
	    private MoneroNetworkType networkType;

		#region API-Surface

		public Task<bool> ValidateAddressAsync(string address)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

			// TODO
            return Task.FromResult(true);
        }

        public Task<object[]> SubscribeWorkerAsync(StratumClient worker)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            
            // setup worker context
            var context = GetWorkerContext(worker);

			// assign unique ExtraNonce1 to worker (miner)
			context.ExtraNonce1 = extraNonceProvider.Next().ToBigEndian().ToString("x4");

            // setup response data
            var responseData = new object[]
            {
                context.ExtraNonce1,
                extraNonceProvider.Size
            };

            return Task.FromResult(responseData);
        }

        public async Task<IShare> SubmitShareAsync(StratumClient worker, object submission, double stratumDifficulty)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.RequiresNonNull(submission, nameof(submission));

	        var submitParams = submission as object[];
			if(submitParams == null)
				throw new StratumException(StratumError.Other, "invalid params");

			// extract params
			var workername = (submitParams[0] as string)?.Trim();
	        var jobId = submitParams[1] as string;
	        var extraNonce2 = submitParams[2] as string;
	        var nTime = submitParams[3] as string;
	        var nonce = submitParams[4] as string;

	        MoneroJob job;

	        lock (jobLock)
			{
				validJobs.TryGetValue(jobId, out job);
			}

			if(job == null)
		        throw new StratumException(StratumError.JobNotFound, "job not found");

			// under testnet or regtest conditions network difficulty may be lower than statum diff
	        var minDiff = Math.Min(networkStats.Difficulty, stratumDifficulty);

			// get worker context
			var context = GetWorkerContext(worker);

			// validate & process
			var share = job.ProcessShare(context.ExtraNonce1, extraNonce2, nTime, nonce, minDiff);

			// if block candidate, submit & check if accepted by network
			if (share.IsBlockCandidate)
			{
				//logger.Info(() => $"[{LogCategory}] Submitting block {share.BlockHash}");

				var acceptResponse = await SubmitBlockAsync(share);

				// is it still a block candidate?
				share.IsBlockCandidate = acceptResponse.Accepted;

				if (share.IsBlockCandidate)
				{
					//logger.Info(() => $"[{LogCategory}] Daemon accepted block {share.BlockHash}");

					// persist the coinbase transaction-hash to allow the payment processor 
					// to verify later on that the pool has received the reward for the block
					share.TransactionConfirmationData = acceptResponse.CoinbaseTransaction;
				}

				else
				{
					// clear fields that no longer apply
					share.TransactionConfirmationData = null;
				}
			}

			// enrich share with common data
	        share.PoolId = poolConfig.Id;
	        share.IpAddress = worker.RemoteEndpoint.Address.ToString();
			share.Worker = workername;
	        share.NetworkDifficulty = networkStats.Difficulty;
			share.Created = DateTime.UtcNow;

			return share;
        }

	    public NetworkStats NetworkStats => networkStats;
 
		#endregion // API-Surface

		#region Overrides

		protected override string LogCategory => "Monero Job Manager";

		protected override async Task<bool> IsDaemonHealthy()
        {
            var responses = await daemon.ExecuteCmdAllAsync<GetInfoResponse>(MDC.GetInfo);

            return responses.All(x => x.Error == null);
        }

	    protected override async Task<bool> IsDaemonConnected()
	    {
		    var response = await daemon.ExecuteCmdAnyAsync<GetInfoResponse>(MDC.GetInfo);

		    return response.Error == null && response.Response.OutgoingConnectionsCount > 0;
	    }

		protected override async Task EnsureDaemonsSynchedAsync()
        {
            var syncPendingNotificationShown = false;

            while (true)
            {
                var responses = await daemon.ExecuteCmdAllAsync<GetBlockTemplateResponse>(
                    MDC.GetBlockTemplate, new JObject());

                var isSynched = responses.All(x => x.Error == null || x.Error.Code != -9);

                if (isSynched)
                {
                    logger.Info(() => $"[{LogCategory}] All daemons synched with blockchain");
                    break;
                }

                if(!syncPendingNotificationShown)
                { 
                    logger.Info(() => $"[{LogCategory}] Daemons still syncing with network. Manager will be started once synced");
                    syncPendingNotificationShown = true;
                }

                await ShowDaemonSyncProgressAsync();

                // delay retry by 5s
                await Task.Delay(5000);
            }
        }
		
        protected override async Task PostStartInitAsync()
        {
	        var commands = new[]
	        {
		        new DaemonCmd(MDC.GetInfo)
	        };

	        var results = await daemon.ExecuteBatchAnyAsync(commands);

	        if (results.Any(x => x.Error != null))
	        {
		        var resultList = results.ToList();
		        var errors = results.Where(x => x.Error != null && commands[resultList.IndexOf(x)].Method != MDC.SubmitBlock).ToArray();

		        if (errors.Any())
			        logger.ThrowLogPoolStartupException($"Init RPC failed: {string.Join(", ", errors.Select(y => y.Error.Message))}", LogCategory);
	        }

	        // extract results
	        var infoResponse = results[0].Response.ToObject<GetInfoResponse>();

			// chain detection
		    networkType = infoResponse.IsTestnet ? MoneroNetworkType.Test : MoneroNetworkType.Main;

			// update stats
			networkStats.RewardType = "POW";

            await UpdateNetworkStats();

	        SetupCrypto();
		}

	    protected override async Task<bool> UpdateJobs(bool forceUpdate)
        {
			var response = await GetBlockTemplateAsync();

	        // may happen if daemon is currently not connected to peers
	        if (response.Error != null)
			{ 
		        logger.Warn(() => $"[{LogCategory}] Unable to update job. Daemon responded with: {response.Error.Message} Code {response.Error.Code}");
				return false;
			}

	        var blockTemplate = response.Response;

	        lock (jobLock)
	        {
		        var isNew = currentJob == null ||
		                (currentJob.BlockTemplate.PreviousBlockhash != blockTemplate.PreviousBlockhash ||
		                 currentJob.BlockTemplate.Height < blockTemplate.Height);

		        if (isNew || forceUpdate)
		        {
			        currentJob = new MoneroJob(blockTemplate, NextJobId(),
						poolConfig, clusterConfig, networkType, extraNonceProvider);

			        currentJob.Init();

			        if (isNew)
			        {
				        validJobs.Clear();

				        // update stats
						networkStats.LastBlockTime = DateTime.UtcNow;
			        }

			        validJobs[currentJob.JobId] = currentJob;
		        }

		        return isNew;
	        }
		}

		protected override object GetJobParamsForStratum(bool isNew)
        {
	        lock (jobLock)
	        {
		        return currentJob?.GetJobParams(isNew);
	        }
        }

		#endregion // Overrides

		private async Task<DaemonResponse<GetBlockTemplateResponse>> GetBlockTemplateAsync()
        {
	        var request = new GetBlockTemplateRequest
	        {
		        WalletAddress = poolConfig.Address,
		        ReservedOffset = 60,
	        };

			var result = await daemon.ExecuteCmdAnyAsync<GetBlockTemplateResponse>(
	            MDC.GetBlockTemplate, request);

			return result;
        }

		private async Task ShowDaemonSyncProgressAsync()
        {
            var infos = await daemon.ExecuteCmdAllAsync<GetInfoResponse>(MDC.GetInfo);
	        var firstValidResponse = infos.FirstOrDefault(x => x.Error == null && x.Response != null)?.Response;

			if (firstValidResponse != null)
            {
				var lowestHeight = infos.Where(x => x.Error == null && x.Response != null)
		            .Min(x => x.Response.Height);

	            var totalBlocks = firstValidResponse.TargetHeight;
		        var percent = ((double) lowestHeight / totalBlocks) * 100;

		        logger.Info(() => $"[{LogCategory}] Daemons have downloaded {percent:0.00}% of blockchain from {firstValidResponse.OutgoingConnectionsCount} peers");
			}
        }

		private Task<(bool Accepted, string CoinbaseTransaction)> SubmitBlockAsync(ShareBase share)
	    {
			//// execute command batch
			//   var results = await daemon.ExecuteBatchAnyAsync(
			//	new DaemonCmd(MDC.SubmitBlock, new[] { share.BlockHex }) :
			//    new DaemonCmd(MDC.GetBlock, new[] { share.BlockHash }));

			//// did submission succeed?
			//   var submitResult = results[0];
			//   var submitError = submitResult.Error?.Message ?? submitResult.Response?.ToString();

			//if (!string.IsNullOrEmpty(submitError))
			//   {
			//    logger.Warn(()=> $"[{LogCategory}] Block submission failed with: {submitError}");
			//    return (false, null);
			//   }

			//// was it accepted?
			//   var acceptResult = results[1];
			//var block = acceptResult.Response?.ToObject<DaemonResponses.GetBlockResult>();
			//var accepted = acceptResult.Error == null && block?.Hash == share.BlockHash;
		    //return (accepted, block?.Transactions.FirstOrDefault());

			return Task.FromResult((Accepted: false, CoinbaseTransaction: ""));
	    }

	    protected override async Task UpdateNetworkStats()
	    {
		    var results = await daemon.ExecuteBatchAnyAsync(
			    new DaemonCmd(MDC.GetInfo)
		    );

		    if (results.Any(x => x.Error != null))
		    {
			    var errors = results.Where(x => x.Error != null).ToArray();

			    if (errors.Any())
				    logger.Warn(() => $"[{LogCategory}] Error(s) refreshing network stats: {string.Join(", ", errors.Select(y => y.Error.Message))}");
		    }

		    var infoResponse = results[0].Response.ToObject<GetInfoResponse>();

		    networkStats.BlockHeight = (int) infoResponse.TargetHeight;
		    networkStats.Difficulty = infoResponse.Difficulty;
		    // TODO: networkStats.HashRate = infoResponse.;
		    networkStats.ConnectedPeers = infoResponse.OutgoingConnectionsCount + infoResponse.IncomingConnectionsCount;
	    }

		private void SetupCrypto()
		{
			// TODO
			//coinbaseHasher = sha256d;
			//headerHasher = sha256d;
			//blockHasher = sha256dReverse;
			//difficultyNormalizationFactor = 1;
		}
	}
}
