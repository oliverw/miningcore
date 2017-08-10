using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using MiningCore.Blockchain.Monero.DaemonRequests;
using MiningCore.Blockchain.Monero.DaemonResponses;
using MiningCore.Blockchain.Monero.StratumRequests;
using MiningCore.Configuration;
using MiningCore.DaemonInterface;
using MiningCore.Native;
using MiningCore.Stratum;
using MiningCore.Util;
using MC = MiningCore.Blockchain.Monero.MoneroCommands;
using MWC = MiningCore.Blockchain.Monero.MoneroWalletCommands;
using NLog;

namespace MiningCore.Blockchain.Monero
{
    public class MoneroJobManager : JobManagerBase<MoneroJob>
    {
	    public MoneroJobManager(
            IComponentContext ctx, 
            DaemonClient daemon) : 
			base(ctx, daemon)
        {
	        Contract.RequiresNonNull(ctx, nameof(ctx));
	        Contract.RequiresNonNull(daemon, nameof(daemon));

	        using (var rng = RandomNumberGenerator.Create())
	        {
		        instanceId = new byte[MoneroConstants.InstanceIdSize];
		        rng.GetNonZeroBytes(instanceId);
	        }
		}

        private readonly BlockchainStats blockchainStats = new BlockchainStats();
	    private DaemonEndpointConfig[] daemonEndpoints;
	    private DaemonEndpointConfig[] walletDaemonEndpoints;
	    private DaemonClient walletDaemon;
	    private MoneroNetworkType networkType;
	    protected DateTime? lastBlockUpdate;
	    private readonly byte[] instanceId;
	    private uint poolAddressBase58Prefix;

		#region API-Surface

		public IObservable<Unit> Blocks { get; private set; }

	    public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
	    {
		    poolAddressBase58Prefix = LibCryptonote.DecodeAddress(poolConfig.Address);

			// extract standard daemon endpoints
			daemonEndpoints = poolConfig.Daemons
			    .Where(x => string.IsNullOrEmpty(x.Category))
			    .ToArray();

		    // extract wallet daemon endpoints
		    walletDaemonEndpoints = poolConfig.Daemons
			    .Where(x => x.Category?.ToLower() == MoneroConstants.WalletDaemonCategory)
			    .ToArray();

		    base.Configure(poolConfig, clusterConfig);
	    }

		public bool ValidateAddress(string address)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

	        if (address.Length != MoneroConstants.AddressLength)
		        return false;

	        var addressPrefix = LibCryptonote.DecodeAddress(address);
	        if (addressPrefix != poolAddressBase58Prefix)
		        return false;

			return true;
        }

	    public BlockchainStats BlockchainStats => blockchainStats;

	    public void PrepareWorkerJob(MoneroWorkerJob workerJob, out string blob, out string target)
	    {
		    blob = null;
		    target = null;

		    lock (jobLock)
		    {
			    currentJob?.PrepareWorkerJob(workerJob, out blob, out target);
		    }
	    }

	    public async Task<IShare> SubmitShareAsync(StratumClient<MoneroWorkerContext> worker, 
			MoneroSubmitShareRequest request, MoneroWorkerJob workerJob, double stratumDifficulty)
	    {
		    MoneroJob job;

		    lock (jobLock)
		    {
			    if(workerJob.Height != currentJob.BlockTemplate.Height)
				    throw new StratumException(StratumError.MinusOne, "block expired");

			    job = currentJob;
		    }

		    // validate & process
		    var share = job?.ProcessShare(request.Nonce, workerJob.ExtraNonce, request.Hash, stratumDifficulty);

		    // if block candidate, submit & check if accepted by network
		    if (share.IsBlockCandidate)
		    {
			    logger.Info(() => $"[{LogCat}] Submitting block {share.BlobHash.Substring(0, 6)}");

			    share.IsBlockCandidate = await SubmitBlockAsync(share);

			    if (share.IsBlockCandidate)
			    {
				    logger.Info(() => $"[{LogCat}] Daemon accepted block {share.BlobHash.Substring(0, 6)}");

					share.TransactionConfirmationData = share.BlobHash;
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
		    share.Miner = worker.Context.MinerName;
		    share.Worker = worker.Context.WorkerName;
		    share.NetworkDifficulty = blockchainStats.NetworkDifficulty;
		    share.Created = DateTime.UtcNow;

		    return share;
	    }

		#endregion // API-Surface

		#region Overrides

		protected override string LogCat => "Monero Job Manager";

		protected override void ConfigureDaemons()
	    {
			daemon.Configure(daemonEndpoints, MoneroConstants.DaemonRpcLocation);

			// also setup wallet daemon
			walletDaemon = ctx.Resolve<DaemonClient>();
			walletDaemon.Configure(walletDaemonEndpoints, MoneroConstants.DaemonRpcLocation);
		}

		protected override async Task<bool> IsDaemonHealthy()
        {
            var responses = await daemon.ExecuteCmdAllAsync<GetInfoResponse>(MC.GetInfo);

            return responses.All(x => x.Error == null);
        }

	    protected override async Task<bool> IsDaemonConnected()
	    {
		    var response = await daemon.ExecuteCmdAnyAsync<GetInfoResponse>(MC.GetInfo);

		    return response.Error == null && response.Response.OutgoingConnectionsCount > 0;
	    }

		protected override async Task EnsureDaemonsSynchedAsync()
        {
            var syncPendingNotificationShown = false;

            while (true)
            {
	            var request = new GetBlockTemplateRequest
	            {
		            WalletAddress = poolConfig.Address,
					ReserveSize = MoneroConstants.ReserveSize,
				};

				var responses = await daemon.ExecuteCmdAllAsync<GetBlockTemplateResponse>(
                    MC.GetBlockTemplate, request);

                var isSynched = responses.All(x => x.Error == null || x.Error.Code != -9);

                if (isSynched)
                {
                    logger.Info(() => $"[{LogCat}] All daemons synched with blockchain");
                    break;
                }

                if(!syncPendingNotificationShown)
                { 
                    logger.Info(() => $"[{LogCat}] Daemons still syncing with network. Manager will be started once synced");
                    syncPendingNotificationShown = true;
                }

                await ShowDaemonSyncProgressAsync();

                // delay retry by 5s
                await Task.Delay(5000);
            }
        }
		
        protected override async Task PostStartInitAsync()
        {
	        var infoResponse = await daemon.ExecuteCmdAnyAsync(MC.GetInfo);
	        var addressResponse = await walletDaemon.ExecuteCmdAnyAsync<GetAddressResponse>(MWC.GetAddress);

			if (infoResponse.Error != null)
			    logger.ThrowLogPoolStartupException($"Init RPC failed: {infoResponse.Error.Message} (Code {infoResponse.Error.Code})", LogCat);

	        if (addressResponse.Response?.Address != poolConfig.Address)
		        logger.ThrowLogPoolStartupException($"Wallet-Daemon does not own pool-address '{poolConfig.Address}'", LogCat);

			var info = infoResponse.Response.ToObject<GetInfoResponse>();

			// chain detection
		    networkType = info.IsTestnet ? MoneroNetworkType.Test : MoneroNetworkType.Main;

			// update stats
			blockchainStats.RewardType = "POW";
	        blockchainStats.NetworkType = networkType.ToString();

			await UpdateNetworkStats();

	        SetupJobUpdates();
		}

		protected virtual void SetupJobUpdates()
	    {
			// periodically update block-template from daemon
			Blocks = Observable.Interval(TimeSpan.FromMilliseconds(poolConfig.BlockRefreshInterval))
			    .Select(_ => Observable.FromAsync(UpdateJob))
			    .Concat()
			    .Do(isNew =>
			    {
				    if (isNew)
					    logger.Info(() => $"[{LogCat}] New block detected");
			    })
			    .Where(isNew => isNew)
				.Select(_=> Unit.Default)
			    .Publish()
			    .RefCount();
	    }

		#endregion // Overrides

		protected async Task<bool> UpdateJob()
        {
	        try
	        {
		        var response = await GetBlockTemplateAsync();

		        // may happen if daemon is currently not connected to peers
		        if (response.Error != null)
		        {
			        logger.Warn(() => $"[{LogCat}] Unable to update job. Daemon responded with: {response.Error.Message} Code {response.Error.Code}");
			        return false;
		        }

		        var blockTemplate = response.Response;

		        lock (jobLock)
		        {
			        var isNew = currentJob == null ||
			                    (currentJob.BlockTemplate.PreviousBlockhash != blockTemplate.PreviousBlockhash ||
			                     currentJob.BlockTemplate.Height < blockTemplate.Height);

			        if (isNew)
			        {
				        currentJob = new MoneroJob(blockTemplate, instanceId, NextJobId(),
					        poolConfig, clusterConfig, networkType);

				        currentJob.Init();

				        // update stats
				        blockchainStats.LastNetworkBlockTime = DateTime.UtcNow;
			        }

			        return isNew;
		        }
	        }

			catch (Exception ex)
	        {
		        logger.Error(ex, () => $"[{LogCat}] Error during {nameof(UpdateJob)}");
	        }

	        return false;
        }

		private async Task<DaemonResponse<GetBlockTemplateResponse>> GetBlockTemplateAsync()
        {
	        var request = new GetBlockTemplateRequest
	        {
		        WalletAddress = poolConfig.Address,
		        ReserveSize = MoneroConstants.ReserveSize,
	        };

			return await daemon.ExecuteCmdAnyAsync<GetBlockTemplateResponse>(MC.GetBlockTemplate, request);
        }

		private async Task ShowDaemonSyncProgressAsync()
        {
            var infos = await daemon.ExecuteCmdAllAsync<GetInfoResponse>(MC.GetInfo);
	        var firstValidResponse = infos.FirstOrDefault(x => x.Error == null && x.Response != null)?.Response;

			if (firstValidResponse != null)
            {
				var lowestHeight = infos.Where(x => x.Error == null && x.Response != null)
		            .Min(x => x.Response.Height);

	            var totalBlocks = firstValidResponse.TargetHeight;
		        var percent = ((double) lowestHeight / totalBlocks) * 100;

		        logger.Info(() => $"[{LogCat}] Daemons have downloaded {percent:0.00}% of blockchain from {firstValidResponse.OutgoingConnectionsCount} peers");
			}
        }

		private async Task<bool> SubmitBlockAsync(MoneroShare share)
		{
		    var response = await daemon.ExecuteCmdAnyAsync<SubmitResponse>(MC.SubmitBlock, new[] { share.BlobHex });

			if (response.Error != null || response?.Response?.Status != "OK")
			{
				var error = response.Error?.Message ?? response.Response?.Status;

				logger.Warn(() => $"[{LogCat}] Block submission failed with: {error}");
				return false;
			}

		    return true;
	    }

		protected async Task UpdateNetworkStats()
	    {
		    var infoResponse = await daemon.ExecuteCmdAnyAsync(MC.GetInfo);

		    if (infoResponse.Error != null)
			    logger.Warn(() => $"[{LogCat}] Error(s) refreshing network stats: {infoResponse.Error.Message} (Code {infoResponse.Error.Code})");

		    var info = infoResponse.Response.ToObject<GetInfoResponse>();

		    blockchainStats.BlockHeight = (int) info.TargetHeight;
		    blockchainStats.NetworkDifficulty = info.Difficulty;
		    blockchainStats.NetworkHashRate = (double) info.Difficulty / info.Target;
		    blockchainStats.ConnectedPeers = info.OutgoingConnectionsCount + info.IncomingConnectionsCount;
	    }
    }
}
