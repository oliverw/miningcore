using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Blockchain.Ergo.Configuration;
using NLog;
using Miningcore.Configuration;
using Miningcore.DaemonInterface;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
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
            IHttpClientFactory httpClientFactory,
            IExtraNonceProvider extraNonceProvider) :
            base(ctx, messageBus)
        {
            Contract.RequiresNonNull(httpClientFactory, nameof(httpClientFactory));
            Contract.RequiresNonNull(clock, nameof(clock));

            this.clock = clock;
            this.extraNonceProvider = extraNonceProvider;
            this.httpClientFactory = httpClientFactory;
        }

        private ErgoCoinTemplate coin;
        private ErgoClient daemon;
        protected string network;
        protected TimeSpan jobRebroadcastTimeout;
        protected DateTime? lastJobRebroadcast;
        protected readonly List<ErgoJob> validJobs = new();
        protected int maxActiveJobs = 4;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly IExtraNonceProvider extraNonceProvider;
        private readonly IMasterClock clock;
        private ErgoPoolConfigExtra extraPoolConfig;

        protected virtual void SetupJobUpdates()
        {
            jobRebroadcastTimeout = TimeSpan.FromSeconds(Math.Max(1, poolConfig.JobRebroadcastTimeout));
            var blockFound = blockFoundSubject.Synchronize();
            var pollTimerRestart = blockFoundSubject.Synchronize();

            var triggers = new List<IObservable<(bool Force, string Via, string Data)>>
            {
                blockFound.Select(x => (false, JobRefreshBy.BlockFound, (string) null))
            };

            if(true) // extraPoolConfig?.BtStream == null)
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

                // periodically update transactions for current template
                if(poolConfig.JobRebroadcastTimeout > 0)
                {
                    triggers.Add(Observable.Timer(jobRebroadcastTimeout)
                        .TakeUntil(pollTimerRestart)
                        .Select(_ => (true, JobRefreshBy.PollRefresh, (string) null))
                        .Repeat());
                }
            }

            //else
            //{
            //    var btStream = BtStreamSubscribe(extraPoolConfig.BtStream);

            //    if(poolConfig.JobRebroadcastTimeout > 0)
            //    {
            //        var interval = TimeSpan.FromSeconds(Math.Max(1, poolConfig.JobRebroadcastTimeout - 0.1d));

            //        triggers.Add(btStream
            //            .Select(json =>
            //            {
            //                var force = !lastJobRebroadcast.HasValue || (clock.Now - lastJobRebroadcast >= interval);
            //                return (force, !force ? JobRefreshBy.BlockTemplateStream : JobRefreshBy.BlockTemplateStreamRefresh, json);
            //            })
            //            .Publish()
            //            .RefCount());
            //    }

            //    else
            //    {
            //        triggers.Add(btStream
            //            .Select(json => (false, JobRefreshBy.BlockTemplateStream, json))
            //            .Publish()
            //            .RefCount());
            //    }

            //    // get initial blocktemplate
            //    triggers.Add(Observable.Interval(TimeSpan.FromMilliseconds(1000))
            //        .Select(_ => (false, JobRefreshBy.Initial, (string) null))
            //        .TakeWhile(_ => !hasInitialBlockTemplate));
            //}

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

        protected async Task<(bool IsNew, bool Force)> UpdateJob(bool forceUpdate, string via = null, string json = null)
        {
            logger.LogInvoke();

            try
            {
                if(forceUpdate)
                    lastJobRebroadcast = clock.Now;

                var blockTemplate = string.IsNullOrEmpty(json) ?
                    await GetBlockTemplateAsync() :
                    GetBlockTemplateFromJson(json);

                var job = currentJob;

                var isNew = job == null ||
                            (blockTemplate != null &&
                             (job.BlockTemplate?.Work.Msg != blockTemplate.Work.Msg ||
                              blockTemplate.Work?.Height > job.BlockTemplate.Work.Height));

                if(isNew)
                    messageBus.NotifyChainHeight(poolConfig.Id, blockTemplate.Work.Height, poolConfig.Template);

                if(isNew || forceUpdate)
                {
                    job = new ErgoJob();

                    job.Init(blockTemplate, NextJobId(), poolConfig, clusterConfig, clock, network);

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
                        //BlockchainStats.NextNetworkTarget = blockTemplate.Target;
                        //BlockchainStats.NextNetworkBits = blockTemplate.Bits;
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

            catch(Exception ex)
            {
                logger.Error(ex, () => $"Error during {nameof(UpdateJob)}");
            }

            return (false, forceUpdate);
        }

        protected async Task<ErgoBlockTemplate> GetBlockTemplateAsync()
        {
            logger.LogInvoke();

            var infoTask = daemon.GetNodeInfoAsync();
            var miningCandidateTask = daemon.MiningRequestBlockCandidateAsync(CancellationToken.None);

            await Task.WhenAll(infoTask, miningCandidateTask);

            var info = infoTask.Result;
            var work = miningCandidateTask.Result;

            return new ErgoBlockTemplate(work, info);
        }

        protected ErgoBlockTemplate GetBlockTemplateFromJson(string json)
        {
            logger.LogInvoke();

            return JsonConvert.DeserializeObject<ErgoBlockTemplate>(json);
        }

        protected async Task ShowDaemonSyncProgressAsync()
        {
            var info = await Guard(() => daemon.GetNodeInfoAsync(),
                ex => logger.Debug(ex));

            if(info?.FullHeight.HasValue == true && info.HeadersHeight.HasValue)
            {
                var percent = (double) info.FullHeight.Value / info.HeadersHeight.Value * 100;

                logger.Info(() => $"Daemon has downloaded {percent:0.00}% of blockchain from {info.PeersCount} peers");
            }

            else if(!string.IsNullOrEmpty(info?.BestFullHeaderId))
                logger.Info(() => $"Daemon is downloading headers ...");

            else
                logger.Info(() => $"Waiting for daemon to resume syncing ...");
        }

        protected async Task UpdateNetworkStatsAsync()
        {
            logger.LogInvoke();

            var info = await Guard(() => daemon.GetNodeInfoAsync(),
                ex => logger.Debug(ex));

            if(info != null)
            {
                BlockchainStats.ConnectedPeers = info.PeersCount;

                // Network difficulty
                if(info.Difficulty.Type == JTokenType.Object)
                {
                    if(((JObject) info.Difficulty).TryGetValue("proof-of-work", out var diffVal))
                        BlockchainStats.NetworkDifficulty = diffVal.Value<double>();
                }

                else
                    BlockchainStats.NetworkDifficulty = info.Difficulty.Value<double>();

                // TODO: BlockchainStats.NetworkHashrate = info.HeadersScore
            }
        }

        protected void ConfigureRewards()
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

        #region API-Surface

        public IObservable<object> Jobs { get; private set; }
        public BlockchainStats BlockchainStats { get; } = new();
        public string Network => network;

        public ErgoCoinTemplate Coin => coin;

        public object[] GetSubscriberData(StratumConnection worker)
        {
            throw new NotImplementedException();
        }

        public ValueTask<Share> SubmitShareAsync(StratumConnection worker, object submission, double stratumDifficultyBase, CancellationToken ct)
        {
            throw new NotImplementedException();
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

            // chain detection
            var m = Regex.Match(info.Name, "ergo-([^-]+)-.+");
            if(!m.Success)
                logger.ThrowLogPoolStartupException($"Unable to identify network type ({info.Name}");

            network = m.Groups[1].Value.ToLower();

            // Payment-processing setup
            if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
            {
                ConfigureRewards();
            }

            // update stats
            BlockchainStats.NetworkType = network;
            BlockchainStats.RewardType = "POW";

            await UpdateNetworkStatsAsync();

            // Periodically update network stats
            Observable.Interval(TimeSpan.FromMinutes(10))
                .Select(via => Observable.FromAsync(async () =>
                    await Guard(UpdateNetworkStatsAsync, ex => logger.Error(ex))))
                .Concat()
                .Subscribe();

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
            var epConfig = poolConfig.Daemons.First();

            var baseUrl = new UriBuilder(epConfig.Ssl || epConfig.Http2 ? Uri.UriSchemeHttps : Uri.UriSchemeHttp,
                epConfig.Host, epConfig.Port, epConfig.HttpPath);

            daemon = new ErgoClient(baseUrl.ToString(), httpClientFactory.CreateClient());
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
                                info.FullHeight.Value > info.HeadersHeight.Value;

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

        protected object GetJobParamsForStratum(bool isNew)
        {
            var job = currentJob;
            return job?.GetJobParams(isNew);
        }

        #endregion // Overrides
    }
}
