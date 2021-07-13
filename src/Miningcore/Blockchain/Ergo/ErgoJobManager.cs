using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Miningcore.Blockchain.Ergo.Configuration;
using NLog;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Contract = Miningcore.Contracts.Contract;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Ergo
{
    public class ErgoJobManager : JobManagerBase<ErgoJob>
    {
        public ErgoJobManager(
            IComponentContext ctx,
            IMessageBus messageBus,
            IMasterClock clock,
            IExtraNonceProvider extraNonceProvider) :
            base(ctx, messageBus)
        {
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(extraNonceProvider, nameof(extraNonceProvider));

            this.clock = clock;
            this.extraNonceProvider = extraNonceProvider;

            extraNonceSize = 8 - extraNonceProvider.ByteSize;
        }

        private ErgoCoinTemplate coin;
        private ErgoClient daemon;
        private string network;
        private readonly List<ErgoJob> validJobs = new();
        private int maxActiveJobs = 4;
        private readonly int extraNonceSize;
        private readonly IExtraNonceProvider extraNonceProvider;
        private readonly IMasterClock clock;
        private ErgoPoolConfigExtra extraPoolConfig;
        private int blockVersion;

        private void SetupJobUpdates()
        {
            var blockFound = blockFoundSubject.Synchronize();
            var pollTimerRestart = blockFoundSubject.Synchronize();

            var triggers = new List<IObservable<(bool Force, string Via, string Data)>>
            {
                blockFound.Select(x => (false, JobRefreshBy.BlockFound, (string) null))
            };

            if(extraPoolConfig?.BtStream == null)
            {
                if(poolConfig.BlockRefreshInterval > 0)
                {
                    // periodically update block-template
                    var pollingInterval = poolConfig.BlockRefreshInterval > 0 ? poolConfig.BlockRefreshInterval : 1000;

                    triggers.Add(Observable.Timer(TimeSpan.FromMilliseconds(pollingInterval))
                        .TakeUntil(pollTimerRestart)
                        .Select(_ => (false, JobRefreshBy.Poll, (string) null))
                        .Repeat());
                }

                else
                {
                    // get initial blocktemplate
                    triggers.Add(Observable.Interval(TimeSpan.FromMilliseconds(1000))
                        .Select(_ => (false, JobRefreshBy.Initial, (string) null))
                        .TakeWhile(_ => !hasInitialBlockTemplate));
                }
            }

            else
            {
                var btStream = BtStreamSubscribe(extraPoolConfig.BtStream);

                triggers.Add(btStream
                    .Select(json => (false, JobRefreshBy.BlockTemplateStream, json))
                    .Publish()
                    .RefCount());

                // get initial blocktemplate
                triggers.Add(Observable.Interval(TimeSpan.FromMilliseconds(1000))
                    .Select(_ => (false, JobRefreshBy.Initial, (string) null))
                    .TakeWhile(_ => !hasInitialBlockTemplate));
            }

            Jobs = Observable.Merge(triggers)
                .Select(x => Observable.FromAsync(() => UpdateJob(x.Force, x.Via, x.Data)))
                .Concat()
                .Where(x => x.IsNew || x.Force)
                .Do(x =>
                {
                    if(x.IsNew)
                        hasInitialBlockTemplate = true;
                })
                .Select(x => GetJobParamsForStratum(x.IsNew))
                .Publish()
                .RefCount();
        }

        private async Task<(bool IsNew, bool Force)> UpdateJob(bool forceUpdate, string via = null, string json = null)
        {
            logger.LogInvoke();

            try
            {
                var blockTemplate = string.IsNullOrEmpty(json) ?
                    await GetBlockTemplateAsync() :
                    GetBlockTemplateFromJson(json);

                var job = currentJob;

                var isNew = job == null ||
                            (blockTemplate != null &&
                             (job.BlockTemplate?.Msg != blockTemplate.Msg ||
                              blockTemplate?.Height > job.BlockTemplate.Height));

                if(isNew)
                    messageBus.NotifyChainHeight(poolConfig.Id, blockTemplate.Height, poolConfig.Template);

                if(isNew || forceUpdate)
                {
                    job = new ErgoJob();

                    job.Init(blockTemplate, blockVersion, extraNonceSize, NextJobId());

                    lock(jobLock)
                    {
                        validJobs.Insert(0, job);

                        // trim active jobs
                        while(validJobs.Count > maxActiveJobs)
                            validJobs.RemoveAt(validJobs.Count - 1);
                    }

                    if(isNew)
                    {
                        if(via != null)
                            logger.Info(() => $"Detected new block {job.Height} [{via}]");
                        else
                            logger.Info(() => $"Detected new block {job.Height}");

                        // update stats
                        BlockchainStats.LastNetworkBlockTime = clock.Now;
                        BlockchainStats.BlockHeight = job.Height;
                        BlockchainStats.NetworkDifficulty = job.Difficulty;

                        var blockTimeAvg = 120;
                        BlockchainStats.NetworkHashrate = BlockchainStats.NetworkDifficulty / blockTimeAvg;
                    }

                    else
                    {
                        if(via != null)
                            logger.Debug(() => $"Template update {job.Height} [{via}]");
                        else
                            logger.Debug(() => $"Template update {job.Height}");
                    }

                    currentJob = job;
                }

                return (isNew, forceUpdate);
            }

            catch(ApiException<ApiError> ex)
            {
                logger.Error(() => $"Error during {nameof(UpdateJob)}: {ex.Result.Detail ?? ex.Result.Reason}");
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"Error during {nameof(UpdateJob)}");
            }

            return (false, forceUpdate);
        }

        private async Task<WorkMessage> GetBlockTemplateAsync()
        {
            logger.LogInvoke();

            var work = await daemon.MiningRequestBlockCandidateAsync(CancellationToken.None);

            return work;
        }

        private WorkMessage GetBlockTemplateFromJson(string json)
        {
            logger.LogInvoke();

            return JsonConvert.DeserializeObject<WorkMessage>(json);
        }

        private async Task ShowDaemonSyncProgressAsync()
        {
            var info = await Guard(() => daemon.GetNodeInfoAsync(),
                ex => logger.Debug(ex));

            if(info?.FullHeight.HasValue == true && info.HeadersHeight.HasValue)
            {
                var percent = (double) info.FullHeight.Value / info.HeadersHeight.Value * 100;

                logger.Info(() => $"Daemon has downloaded {percent:0.00}% of blockchain from {info.PeersCount} peers");
            }

            else
                logger.Info(() => $"Daemon is downloading headers ...");
        }

        private void ConfigureRewards()
        {
            // Donation to MiningCore development
            if(network == "mainnet" &&
               DevDonation.Addresses.TryGetValue(poolConfig.Template.Symbol, out var address))
            {
                poolConfig.RewardRecipients = poolConfig.RewardRecipients.Concat(new[]
                {
                    new RewardRecipient
                    {
                        Address = address,
                        Percentage = DevDonation.Percent,
                        Type = "dev"
                    }
                }).ToArray();
            }
        }

        private async Task<bool> SubmitBlockAsync(Share share, ErgoJob job, string nonce)
        {
            try
            {
                await daemon.MiningSubmitSolutionAsync(new PowSolutions
                {
                    N = nonce,
                });

                return true;
            }

            catch(ApiException<ApiError> ex)
            {
                logger.Warn(() => $"Block {share.BlockHeight} submission failed with: {ex.Result.Detail ?? ex.Result.Reason ?? ex.Message}");
                messageBus.SendMessage(new AdminNotification("Block submission failed", $"Pool {poolConfig.Id} {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}failed to submit block {share.BlockHeight}: {ex.Result.Detail ?? ex.Result.Reason ?? ex.Message}"));
            }

            catch(Exception ex)
            {
                logger.Warn(() => $"Block {share.BlockHeight} submission failed with: {ex.Message}");
                messageBus.SendMessage(new AdminNotification("Block submission failed", $"Pool {poolConfig.Id} {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}failed to submit block {share.BlockHeight}: {ex.Message}"));
            }

            return false;
        }

        #region API-Surface

        public IObservable<object[]> Jobs { get; private set; }
        public BlockchainStats BlockchainStats { get; } = new();
        public string Network => network;

        public ErgoCoinTemplate Coin => coin;

        public object[] GetSubscriberData(StratumConnection worker)
        {
            Contract.RequiresNonNull(worker, nameof(worker));

            var context = worker.ContextAs<ErgoWorkerContext>();

            // assign unique ExtraNonce1 to worker (miner)
            context.ExtraNonce1 = extraNonceProvider.Next();

            // setup response data
            var responseData = new object[]
            {
                context.ExtraNonce1,
                extraNonceSize,
            };

            return responseData;
        }

        public async ValueTask<Share> SubmitShareAsync(StratumConnection worker, object submission, double stratumDifficultyBase, CancellationToken ct)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.RequiresNonNull(submission, nameof(submission));

            logger.LogInvoke(new[] { worker.ConnectionId });

            if(submission is not object[] submitParams)
                throw new StratumException(StratumError.Other, "invalid params");

            var context = worker.ContextAs<ErgoWorkerContext>();

            // extract params
            var workerValue = (submitParams[0] as string)?.Trim();
            var jobId = submitParams[1] as string;
            var extraNonce2 = submitParams[2] as string;
            var nTime = submitParams[3] as string;
            var nonce = submitParams[4] as string;

            if(string.IsNullOrEmpty(workerValue))
                throw new StratumException(StratumError.Other, "missing or invalid workername");

            ErgoJob job;

            lock(jobLock)
            {
                job = validJobs.FirstOrDefault(x => x.JobId == jobId);
            }

            if(job == null)
                throw new StratumException(StratumError.JobNotFound, "job not found");

            // extract worker/miner/payoutid
            var split = workerValue.Split('.');
            var minerName = split[0];
            var workerName = split.Length > 1 ? split[1] : null;

            // validate & process
            var share = job.ProcessShare(worker, extraNonce2, nTime, nonce);

            // enrich share with common data
            share.PoolId = poolConfig.Id;
            share.IpAddress = worker.RemoteEndpoint.Address.ToString();
            share.Miner = minerName;
            share.Worker = workerName;
            share.UserAgent = context.UserAgent;
            share.Source = clusterConfig.ClusterName;
            share.Created = clock.Now;

            // if block candidate, submit & check if accepted by network
            if(share.IsBlockCandidate)
            {
                logger.Info(() => $"Submitting block {share.BlockHeight} [{share.BlockHash}]");

                var acceptResponse = await SubmitBlockAsync(share, job, nonce);

                // is it still a block candidate?
                share.IsBlockCandidate = acceptResponse;

                if(share.IsBlockCandidate)
                {
                    logger.Info(() => $"Daemon accepted block {share.BlockHeight} [{share.BlockHash}] submitted by {minerName}");

                    OnBlockFound();

                    // persist the nonce to make block unlocking a bit more reliable
                    share.TransactionConfirmationData = nonce;
                }

                else
                {
                    // clear fields that no longer apply
                    share.TransactionConfirmationData = null;
                }
            }

            return share;
        }

        public async Task<bool> ValidateAddress(string address, CancellationToken ct)
        {
            if(string.IsNullOrEmpty(address))
                return false;

            var validity = await Guard(() => daemon.CheckAddressValidityAsync(address, ct),
                ex => logger.Debug(ex));

            return validity?.IsValid == true;
        }

        #endregion // API-Surface

        #region Overrides

        protected override async Task PostStartInitAsync(CancellationToken ct)
        {
            // validate pool address
            if(string.IsNullOrEmpty(poolConfig.Address))
                logger.ThrowLogPoolStartupException($"Pool address is not configured");

            var validity = await Guard(() => daemon.CheckAddressValidityAsync(poolConfig.Address, ct),
                ex=> logger.ThrowLogPoolStartupException($"Error validating pool address: {ex}"));

            if(!validity.IsValid)
                logger.ThrowLogPoolStartupException($"Daemon reports pool address {poolConfig.Address} as invalid: {validity.Error}");

            var info = await Guard(() => daemon.GetNodeInfoAsync(ct),
                ex=> logger.ThrowLogPoolStartupException($"Daemon reports: {ex.Message}"));

            blockVersion = info.Parameters.BlockVersion;

            // chain detection
            var m = ErgoConstants.RegexChain.Match(info.Name);
            if(!m.Success)
                logger.ThrowLogPoolStartupException($"Unable to identify network type ({info.Name}");

            network = m.Groups[1].Value.ToLower();

            // Payment-processing setup
            if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
            {
                // check configured address belongs to wallet
                var walletAddresses = await daemon.WalletAddressesAsync(ct);

                if(!walletAddresses.Contains(poolConfig.Address))
                    logger.ThrowLogPoolStartupException($"Pool address {poolConfig.Address} is not controlled by wallet");

                ConfigureRewards();
            }

            // update stats
            BlockchainStats.NetworkType = network;
            BlockchainStats.RewardType = "POW";

            SetupJobUpdates();
        }

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            coin = poolConfig.Template.As<ErgoCoinTemplate>();

            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<ErgoPoolConfigExtra>();

            if(extraPoolConfig?.MaxActiveJobs.HasValue == true)
                maxActiveJobs = extraPoolConfig.MaxActiveJobs.Value;

            base.Configure(poolConfig, clusterConfig);
        }

        protected override void ConfigureDaemons()
        {
            daemon = ErgoClientFactory.CreateClient(poolConfig, clusterConfig, logger);
        }

        protected override async Task<bool> AreDaemonsHealthyAsync()
        {
            var info = await Guard(() => daemon.GetNodeInfoAsync(),
                ex=> logger.ThrowLogPoolStartupException($"Daemon reports: {ex.Message}"));

            if(info?.IsMining != true)
                logger.ThrowLogPoolStartupException($"Mining is disabled in Ergo Daemon");

            return true;
        }

        protected override async Task<bool> AreDaemonsConnectedAsync()
        {
            var info = await Guard(() => daemon.GetNodeInfoAsync(),
                ex=> logger.Debug(ex));

            return info?.PeersCount > 0;
        }

        protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
        {
            var syncPendingNotificationShown = false;

            while(true)
            {
                var info = await Guard(() => daemon.GetNodeInfoAsync(ct),
                    ex=> logger.Debug(ex));

                var isSynched = info?.FullHeight.HasValue == true && info?.HeadersHeight.HasValue == true &&
                                info.FullHeight.Value >= info.HeadersHeight.Value;

                if(isSynched)
                {
                    logger.Info(() => "Daemon is synced with blockchain");
                    break;
                }

                if(!syncPendingNotificationShown)
                {
                    logger.Info(() => "Daemon is still syncing with network. Manager will be started once synced");
                    syncPendingNotificationShown = true;
                }

                await ShowDaemonSyncProgressAsync();

                // delay retry by 5s
                await Task.Delay(5000, ct);
            }
        }

        private object[] GetJobParamsForStratum(bool isNew)
        {
            var job = currentJob;
            return job?.GetJobParams(isNew);
        }

        #endregion // Overrides
    }
}
