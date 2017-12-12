/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Autofac;
using MiningCore.Blockchain.Monero.DaemonRequests;
using MiningCore.Blockchain.Monero.DaemonResponses;
using MiningCore.Blockchain.Monero.StratumRequests;
using MiningCore.Configuration;
using MiningCore.DaemonInterface;
using MiningCore.Extensions;
using MiningCore.Native;
using MiningCore.Notifications;
using MiningCore.Stratum;
using MiningCore.Time;
using MiningCore.Util;
using Newtonsoft.Json;
using NLog;
using Contract = MiningCore.Contracts.Contract;
using MC = MiningCore.Blockchain.Monero.MoneroCommands;
using MWC = MiningCore.Blockchain.Monero.MoneroWalletCommands;

namespace MiningCore.Blockchain.Monero
{
    public class MoneroJobManager : JobManagerBase<MoneroJob>
    {
        public MoneroJobManager(
            IComponentContext ctx,
            NotificationService notificationService,
            IMasterClock clock) :
            base(ctx)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(notificationService, nameof(notificationService));
            Contract.RequiresNonNull(clock, nameof(clock));

            this.notificationService = notificationService;
            this.clock = clock;

            using(var rng = RandomNumberGenerator.Create())
            {
                instanceId = new byte[MoneroConstants.InstanceIdSize];
                rng.GetNonZeroBytes(instanceId);
            }
        }

        private readonly byte[] instanceId;
        private DaemonEndpointConfig[] daemonEndpoints;
        private DaemonClient daemon;
        private DaemonClient walletDaemon;
        private readonly NotificationService notificationService;
        private readonly IMasterClock clock;
        private MoneroNetworkType networkType;
        private uint poolAddressBase58Prefix;
        private DaemonEndpointConfig[] walletDaemonEndpoints;

        protected async Task<bool> UpdateJob()
        {
            logger.LogInvoke(LogCat);

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
                var job = currentJob;

                var isNew = job == null || job.BlockTemplate.Height < blockTemplate.Height;

                if (isNew)
                {
                    job = new MoneroJob(blockTemplate, instanceId, NextJobId(), poolConfig, clusterConfig);
                    job.Init();
                    currentJob = job;

                    // update stats
                    BlockchainStats.LastNetworkBlockTime = clock.Now;
                }

                return isNew;
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Error during {nameof(UpdateJob)}");
            }

            return false;
        }

        private async Task<DaemonResponse<GetBlockTemplateResponse>> GetBlockTemplateAsync()
        {
            logger.LogInvoke(LogCat);

            var request = new GetBlockTemplateRequest
            {
                WalletAddress = poolConfig.Address,
                ReserveSize = MoneroConstants.ReserveSize
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
                var percent = (double) lowestHeight / totalBlocks * 100;

                logger.Info(() => $"[{LogCat}] Daemons have downloaded {percent:0.00}% of blockchain from {firstValidResponse.OutgoingConnectionsCount} peers");
            }
        }

        private async Task<bool> SubmitBlockAsync(MoneroShare share)
        {
            var response = await daemon.ExecuteCmdAnyAsync<SubmitResponse>(MC.SubmitBlock, new[] { share.BlobHex });

            if (response.Error != null || response?.Response?.Status != "OK")
            {
                var error = response.Error?.Message ?? response.Response?.Status;

                logger.Warn(() => $"[{LogCat}] Block {share.BlockHeight} [{share.BlobHash.Substring(0, 6)}] submission failed with: {error}");
                notificationService.NotifyAdmin("Block submission failed", $"Block {share.BlockHeight} submission failed with: {error}");

                return false;
            }

            return true;
        }

        #region API-Surface

        public IObservable<Unit> Blocks { get; private set; }

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

            logger = LogUtil.GetPoolScopedLogger(typeof(JobManagerBase<MoneroJob>), poolConfig);
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;

            poolAddressBase58Prefix = LibCryptonote.DecodeAddress(poolConfig.Address);
            if (poolAddressBase58Prefix == 0)
                logger.ThrowLogPoolStartupException("Unable to decode pool-address", LogCat);

            // extract standard daemon endpoints
            daemonEndpoints = poolConfig.Daemons
                .Where(x => string.IsNullOrEmpty(x.Category))
                .ToArray();

            // extract wallet daemon endpoints
            walletDaemonEndpoints = poolConfig.Daemons
                .Where(x => x.Category?.ToLower() == MoneroConstants.WalletDaemonCategory)
                .ToArray();

            if (walletDaemonEndpoints.Length == 0)
                logger.ThrowLogPoolStartupException("Wallet-RPC daemon is not configured (Daemon configuration for monero-pools require an additional entry of category \'wallet' pointing to the wallet daemon)", LogCat);

            ConfigureDaemons();
        }

        public bool ValidateAddress(string address)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

            if (address.Length != MoneroConstants.AddressLength[poolConfig.Coin.Type])
                return false;

            var addressPrefix = LibCryptonote.DecodeAddress(address);
            if (addressPrefix != poolAddressBase58Prefix)
                return false;

            return true;
        }

        public BlockchainStats BlockchainStats { get; } = new BlockchainStats();

        public void PrepareWorkerJob(MoneroWorkerJob workerJob, out string blob, out string target)
        {
            blob = null;
            target = null;

            var job = currentJob;

            if (job != null)
            {
                lock(job)
                {
                    job.PrepareWorkerJob(workerJob, out blob, out target);
                }
            }
        }

        public async Task<MoneroShare> SubmitShareAsync(StratumClient worker,
            MoneroSubmitShareRequest request, MoneroWorkerJob workerJob, double stratumDifficultyBase)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.RequiresNonNull(request, nameof(request));
            var context = worker.GetContextAs<MoneroWorkerContext>();

            logger.LogInvoke(LogCat, new[] { worker.ConnectionId });

            var job = currentJob;
            if (workerJob.Height != job?.BlockTemplate.Height)
                throw new StratumException(StratumError.MinusOne, "block expired");

            // validate & process
            var share = job?.ProcessShare(request.Nonce, workerJob.ExtraNonce, request.Hash, worker);

            // if block candidate, submit & check if accepted by network
            if (share.IsBlockCandidate)
            {
                logger.Info(() => $"[{LogCat}] Submitting block {share.BlockHeight} [{share.BlobHash.Substring(0, 6)}]");

                share.IsBlockCandidate = await SubmitBlockAsync(share);

                if (share.IsBlockCandidate)
                {
                    logger.Info(() => $"[{LogCat}] Daemon accepted block {share.BlockHeight} [{share.BlobHash.Substring(0, 6)}]");

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
            share.Miner = context.MinerName;
            share.Worker = context.WorkerName;
            share.PayoutInfo = context.PaymentId;
            share.UserAgent = context.UserAgent;
            share.NetworkDifficulty = job.BlockTemplate.Difficulty;
            share.Created = clock.Now;

            return share;
        }

        public async Task UpdateNetworkStatsAsync()
        {
            logger.LogInvoke(LogCat);

            var infoResponse = await daemon.ExecuteCmdAnyAsync(MC.GetInfo);

            if (infoResponse.Error != null)
                logger.Warn(() => $"[{LogCat}] Error(s) refreshing network stats: {infoResponse.Error.Message} (Code {infoResponse.Error.Code})");

            var info = infoResponse.Response.ToObject<GetInfoResponse>();

            BlockchainStats.BlockHeight = (int) info.Height;
            BlockchainStats.NetworkDifficulty = info.Difficulty;
            BlockchainStats.NetworkHashRate = info.Target > 0 ? (double) info.Difficulty / info.Target : 0;
            BlockchainStats.ConnectedPeers = info.OutgoingConnectionsCount + info.IncomingConnectionsCount;
        }

        #endregion // API-Surface

        #region Overrides

        protected override string LogCat => "Monero Job Manager";

        protected override void ConfigureDaemons()
        {
            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            daemon = new DaemonClient(jsonSerializerSettings);
            daemon.Configure(daemonEndpoints, MoneroConstants.DaemonRpcLocation);

            // also setup wallet daemon
            walletDaemon = new DaemonClient(jsonSerializerSettings);
            walletDaemon.Configure(walletDaemonEndpoints, MoneroConstants.DaemonRpcLocation);
        }

        protected override async Task<bool> AreDaemonsHealthy()
        {
            // test daemons
            var responses = await daemon.ExecuteCmdAllAsync<GetInfoResponse>(MC.GetInfo);

            if (responses.Where(x => x.Error?.InnerException?.GetType() == typeof(DaemonClientException))
                .Select(x => (DaemonClientException) x.Error.InnerException)
                .Any(x => x.Code == HttpStatusCode.Unauthorized))
                logger.ThrowLogPoolStartupException($"Daemon reports invalid credentials", LogCat);

            if (!responses.All(x => x.Error == null))
                return false;

            // test wallet daemons
            var responses2 = await walletDaemon.ExecuteCmdAllAsync<object>(MWC.GetAddress);

            if (responses2.Where(x => x.Error?.InnerException?.GetType() == typeof(DaemonClientException))
                .Select(x => (DaemonClientException) x.Error.InnerException)
                .Any(x => x.Code == HttpStatusCode.Unauthorized))
                logger.ThrowLogPoolStartupException($"Wallet-Daemon reports invalid credentials", LogCat);

            return responses2.All(x => x.Error == null);
        }

        protected override async Task<bool> AreDaemonsConnected()
        {
            var response = await daemon.ExecuteCmdAnyAsync<GetInfoResponse>(MC.GetInfo);

            return response.Error == null && response.Response != null &&
                (response.Response.OutgoingConnectionsCount + response.Response.IncomingConnectionsCount) > 0;
        }

        protected override async Task EnsureDaemonsSynchedAsync()
        {
            var syncPendingNotificationShown = false;

            while(true)
            {
                var request = new GetBlockTemplateRequest
                {
                    WalletAddress = poolConfig.Address,
                    ReserveSize = MoneroConstants.ReserveSize
                };

                var responses = await daemon.ExecuteCmdAllAsync<GetBlockTemplateResponse>(
                    MC.GetBlockTemplate, request);

                var isSynched = responses.All(x => x.Error == null || x.Error.Code != -9);

                if (isSynched)
                {
                    logger.Info(() => $"[{LogCat}] All daemons synched with blockchain");
                    break;
                }

                if (!syncPendingNotificationShown)
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

            // ensure pool owns wallet
            if (addressResponse.Response?.Address != poolConfig.Address)
                logger.ThrowLogPoolStartupException($"Wallet-Daemon does not own pool-address '{poolConfig.Address}'", LogCat);

            var info = infoResponse.Response.ToObject<GetInfoResponse>();

            // chain detection
            networkType = info.IsTestnet ? MoneroNetworkType.Test : MoneroNetworkType.Main;

            ConfigureRewards();

            // update stats
            BlockchainStats.RewardType = "POW";
            BlockchainStats.NetworkType = networkType.ToString();

            await UpdateNetworkStatsAsync();

            SetupJobUpdates();
        }

        private void ConfigureRewards()
        {
            // Donation to MiningCore development
            var devDonation = clusterConfig.DevDonation ?? 0.15m;

            if (devDonation > 0)
            {
                string address = null;

                if (networkType == MoneroNetworkType.Main && poolConfig.Coin.Type == CoinType.XMR)
                    address = MoneroConstants.DevAddress;

                if (!string.IsNullOrEmpty(address))
                {
                    poolConfig.RewardRecipients = poolConfig.RewardRecipients.Concat(new[]
                    {
                        new RewardRecipient
                        {
                            Address = address,
                            Percentage = devDonation,
                        }
                    }).ToArray();
                }
            }
        }

        protected virtual void SetupJobUpdates()
        {
	        if (poolConfig.ExternalStratum)
		        return;

			// periodically update block-template from daemon
			Blocks = Observable.Interval(TimeSpan.FromMilliseconds(poolConfig.BlockRefreshInterval))
                .Select(_ => Observable.FromAsync(UpdateJob))
                .Concat()
                .Do(isNew =>
                {
                    if (isNew)
                        logger.Info(() => $"[{LogCat}] New block {currentJob.BlockTemplate.Height} detected");
                })
                .Where(isNew => isNew)
                .Select(_ => Unit.Default)
                .Publish()
                .RefCount();
        }

        #endregion // Overrides
    }
}
