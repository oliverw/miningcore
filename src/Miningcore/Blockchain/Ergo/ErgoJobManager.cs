using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Stratum;
using Miningcore.Util;
using Contract = Miningcore.Contracts.Contract;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Ergo
{
    public class ErgoJobManager : JobManagerBase<ErgoJob>
    {
        public ErgoJobManager(
            IComponentContext ctx,
            IMessageBus messageBus,
            IHttpClientFactory httpClientFactory,
            IExtraNonceProvider extraNonceProvider) :
            base(ctx, messageBus)
        {
            Contract.RequiresNonNull(httpClientFactory, nameof(httpClientFactory));

            this.extraNonceProvider = extraNonceProvider;
            this.httpClientFactory = httpClientFactory;
        }

        private ErgoCoinTemplate coin;
        private ErgoClient daemon;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly IExtraNonceProvider extraNonceProvider;

        protected async Task ShowDaemonSyncProgressAsync()
        {
            var info = await Guard(() => daemon.GetNodeInfoAsync(),
                ex=> logger.Debug(ex));

            if(info.FullHeight.HasValue && info.HeadersHeight.HasValue)
            {
                var totalBlocks = info.FullHeight.Value;
                var percent = (double) info.HeadersHeight.Value / totalBlocks * 100;

                logger.Info(() => $"Daemon has downloaded {percent:0.00}% of blockchain from {info.PeersCount} peers");
            }

            else
                logger.Info(() => $"Waiting for daemon to resume syncing ...");
        }

        protected async Task UpdateNetworkStatsAsync()
        {
            logger.LogInvoke();

            //try
            //{
            //    var infoResponse = await daemon.ExecuteCmdAnyAsync(logger, CryptonoteCommands.GetInfo);

            //    if(infoResponse.Error != null)
            //        logger.Warn(() => $"Error(s) refreshing network stats: {infoResponse.Error.Message} (Code {infoResponse.Error.Code})");

            //    if(infoResponse.Response != null)
            //    {
            //        var info = infoResponse.Response.ToObject<GetInfoResponse>();

            //        BlockchainStats.NetworkHashrate = info.Target > 0 ? (double) info.Difficulty / info.Target : 0;
            //        BlockchainStats.ConnectedPeers = info.OutgoingConnectionsCount + info.IncomingConnectionsCount;
            //    }
            //}

            //catch(Exception e)
            //{
            //    logger.Error(e);
            //}
        }

        #region API-Surface

        public IObservable<object> Jobs { get; private set; }
        public BlockchainStats BlockchainStats { get; } = new();

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
                ex=> logger.Debug(ex));

            return validity?.IsValid == true;
        }

        #endregion // API-Surface

        #region Overrides

        protected override Task PostStartInitAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            coin = poolConfig.Template.As<ErgoCoinTemplate>();

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

                var isSynched = info?.Difficulty.HasValue == true;

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

        protected Task<(bool IsNew, bool Force)> UpdateJob(bool forceUpdate, string via = null, string json = null)
        {
            return Task.FromResult((true, false));
        }

        protected object GetJobParamsForStratum(bool isNew)
        {
            var job = currentJob;
            return job?.GetJobParams(isNew);
        }

        #endregion // Overrides
    }
}
