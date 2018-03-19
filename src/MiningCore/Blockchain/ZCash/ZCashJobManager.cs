using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.Bitcoin.DaemonResponses;
using MiningCore.Blockchain.ZCash.Configuration;
using MiningCore.Blockchain.ZCash.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.Contracts;
using MiningCore.DaemonInterface;
using MiningCore.Extensions;
using MiningCore.Notifications;
using MiningCore.Stratum;
using MiningCore.Time;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace MiningCore.Blockchain.ZCash
{
    public class ZCashJobManager<TJob> : BitcoinJobManager<TJob, ZCashBlockTemplate>
        where TJob : ZCashJob, new()
    {
        public ZCashJobManager(
            IComponentContext ctx,
            NotificationService notificationService,
            IMasterClock clock,
            IExtraNonceProvider extraNonceProvider) : base(ctx, notificationService, clock, extraNonceProvider)
        {
            getBlockTemplateParams = new object[]
            {
                new
                {
                    capabilities = new[] { "coinbasetxn", "workid", "coinbase/append" },
                }
            };
        }

        private ZCashPoolConfigExtra zcashExtraPoolConfig;

        #region Overrides of JobManagerBase<TJob>

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            zcashExtraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<ZCashPoolConfigExtra>();

            base.Configure(poolConfig, clusterConfig);
        }

        #endregion

        public override async Task<bool> ValidateAddressAsync(string address)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

            // handle t-addr
            if (await base.ValidateAddressAsync(address))
                return true;

            // handle z-addr
            var result = await daemon.ExecuteCmdAnyAsync<ValidateAddressResponse>(
                ZCashCommands.ZValidateAddress, new[] { address });

            return result.Response != null && result.Response.IsValid;
        }

        protected override async Task<DaemonResponse<ZCashBlockTemplate>> GetBlockTemplateAsync()
        {
            logger.LogInvoke(LogCat);

            var subsidyResponse = await daemon.ExecuteCmdAnyAsync<ZCashBlockSubsidy>(BitcoinCommands.GetBlockSubsidy);

            var result = await daemon.ExecuteCmdAnyAsync<ZCashBlockTemplate>(
                BitcoinCommands.GetBlockTemplate, getBlockTemplateParams);

            if (subsidyResponse.Error == null && result.Error == null && result.Response != null)
                result.Response.Subsidy = subsidyResponse.Response;

            return result;
        }

        public override object[] GetSubscriberData(StratumClient worker)
        {
            Contract.RequiresNonNull(worker, nameof(worker));

            var context = worker.GetContextAs<BitcoinWorkerContext>();

            // assign unique ExtraNonce1 to worker (miner)
            context.ExtraNonce1 = extraNonceProvider.Next();

            // setup response data
            var responseData = new object[]
            {
                context.ExtraNonce1
            };

            return responseData;
        }

        protected override IDestination AddressToDestination(string address)
        {
            var decoded = Encoders.Base58.DecodeData(address);
            var hash = decoded.Skip(2).Take(20).ToArray();
            var result = new KeyId(hash);
            return result;
        }

        public override async Task<Share> SubmitShareAsync(StratumClient worker, object submission,
            double stratumDifficultyBase)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.RequiresNonNull(submission, nameof(submission));

            logger.LogInvoke(LogCat, new[] { worker.ConnectionId });

            if (!(submission is object[] submitParams))
                throw new StratumException(StratumError.Other, "invalid params");

            var context = worker.GetContextAs<BitcoinWorkerContext>();

            // extract params
            var workerValue = (submitParams[0] as string)?.Trim();
            var jobId = submitParams[1] as string;
            var nTime = submitParams[2] as string;
            var extraNonce2 = submitParams[3] as string;
            var solution = submitParams[4] as string;

            if (string.IsNullOrEmpty(workerValue))
                throw new StratumException(StratumError.Other, "missing or invalid workername");

            if (string.IsNullOrEmpty(solution))
                throw new StratumException(StratumError.Other, "missing or invalid solution");

            ZCashJob job;

            lock(jobLock)
            {
                job = validJobs.FirstOrDefault(x => x.JobId == jobId);
            }

            if (job == null)
                throw new StratumException(StratumError.JobNotFound, "job not found");

            // extract worker/miner/payoutid
            var split = workerValue.Split('.');
            var minerName = split[0];
            var workerName = split.Length > 1 ? split[1] : null;

            // validate & process
            var (share, blockHex) = job.ProcessShare(worker, extraNonce2, nTime, solution);

            // if block candidate, submit & check if accepted by network
            if (share.IsBlockCandidate)
            {
                logger.Info(() => $"[{LogCat}] Submitting block {share.BlockHeight} [{share.BlockHash}]");

                var acceptResponse = await SubmitBlockAsync(share, blockHex);

                // is it still a block candidate?
                share.IsBlockCandidate = acceptResponse.Accepted;

                if (share.IsBlockCandidate)
                {
                    logger.Info(() => $"[{LogCat}] Daemon accepted block {share.BlockHeight} [{share.BlockHash}] submitted by {minerName}");

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
            share.Miner = minerName;
            share.Worker = workerName;
            share.UserAgent = context.UserAgent;
            share.Source = clusterConfig.ClusterName;
            share.NetworkDifficulty = job.Difficulty;
            share.Difficulty = share.Difficulty / ShareMultiplier;
            share.Created = clock.Now;

            return share;
        }
    }
}
