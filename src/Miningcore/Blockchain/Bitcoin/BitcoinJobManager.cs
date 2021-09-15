using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Crypto;
using Miningcore.DaemonInterface;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace Miningcore.Blockchain.Bitcoin
{
    public class BitcoinJobManager : BitcoinJobManagerBase<BitcoinJob>
    {
        public BitcoinJobManager(
            IComponentContext ctx,
            IMasterClock clock,
            IMessageBus messageBus,
            IExtraNonceProvider extraNonceProvider) :
            base(ctx, clock, messageBus, extraNonceProvider)
        {
        }

        private BitcoinTemplate coin;

        protected override object[] GetBlockTemplateParams()
        {
            var result = base.GetBlockTemplateParams();

            if(coin.BlockTemplateRpcExtraParams != null)
            {
                if(coin.BlockTemplateRpcExtraParams.Type == JTokenType.Array)
                    result = result.Concat(coin.BlockTemplateRpcExtraParams.ToObject<object[]>() ?? Array.Empty<object>()).ToArray();
                else
                    result = result.Concat(new []{ coin.BlockTemplateRpcExtraParams.ToObject<object>()}).ToArray();
            }

            return result;
        }

        protected async Task<DaemonResponse<BlockTemplate>> GetBlockTemplateAsync(CancellationToken ct)
        {
            logger.LogInvoke();

            var result = await daemon.ExecuteCmdAnyAsync<BlockTemplate>(logger,
                BitcoinCommands.GetBlockTemplate, ct, extraPoolConfig?.GBTArgs ?? (object) GetBlockTemplateParams());

            return result;
        }

        protected DaemonResponse<BlockTemplate> GetBlockTemplateFromJson(string json)
        {
            logger.LogInvoke();

            var result = JsonConvert.DeserializeObject<JsonRpcResponse>(json);

            return new DaemonResponse<BlockTemplate>
            {
                Response = result.ResultAs<BlockTemplate>(),
            };
        }

        private BitcoinJob CreateJob()
        {
            return new();
        }

        protected override void PostChainIdentifyConfigure()
        {
            base.PostChainIdentifyConfigure();

            if(poolConfig.EnableInternalStratum == true && coin.HeaderHasherValue is IHashAlgorithmInit hashInit)
            {
                if(!hashInit.DigestInit(poolConfig))
                    logger.Error(()=> $"{hashInit.GetType().Name} initialization failed");
            }
        }

        protected override async Task<(bool IsNew, bool Force)> UpdateJob(CancellationToken ct, bool forceUpdate, string via = null, string json = null)
        {
            logger.LogInvoke();

            try
            {
                if(forceUpdate)
                    lastJobRebroadcast = clock.Now;

                var response = string.IsNullOrEmpty(json) ?
                    await GetBlockTemplateAsync(ct) :
                    GetBlockTemplateFromJson(json);

                // may happen if daemon is currently not connected to peers
                if(response.Error != null)
                {
                    logger.Warn(() => $"Unable to update job. Daemon responded with: {response.Error.Message} Code {response.Error.Code}");
                    return (false, forceUpdate);
                }

                var blockTemplate = response.Response;
                var job = currentJob;

                var isNew = job == null ||
                    (blockTemplate != null &&
                        (job.BlockTemplate?.PreviousBlockhash != blockTemplate.PreviousBlockhash ||
                        blockTemplate.Height > job.BlockTemplate?.Height));

                if(isNew)
                    messageBus.NotifyChainHeight(poolConfig.Id, blockTemplate.Height, poolConfig.Template);

                if(isNew || forceUpdate)
                {
                    job = CreateJob();

                    job.Init(blockTemplate, NextJobId(),
                        poolConfig, extraPoolConfig, clusterConfig, clock, poolAddressDestination, network, isPoS,
                        ShareMultiplier, coin.CoinbaseHasherValue, coin.HeaderHasherValue,
                        !isPoS ? coin.BlockHasherValue : coin.PoSBlockHasherValue ?? coin.BlockHasherValue);

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
                            logger.Info(() => $"Detected new block {blockTemplate.Height} [{via}]");
                        else
                            logger.Info(() => $"Detected new block {blockTemplate.Height}");

                        // update stats
                        BlockchainStats.LastNetworkBlockTime = clock.Now;
                        BlockchainStats.BlockHeight = blockTemplate.Height;
                        BlockchainStats.NetworkDifficulty = job.Difficulty;
                        BlockchainStats.NextNetworkTarget = blockTemplate.Target;
                        BlockchainStats.NextNetworkBits = blockTemplate.Bits;
                    }

                    else
                    {
                        if(via != null)
                            logger.Debug(() => $"Template update {blockTemplate.Height} [{via}]");
                        else
                            logger.Debug(() => $"Template update {blockTemplate.Height}");
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

        protected override object GetJobParamsForStratum(bool isNew)
        {
            var job = currentJob;
            return job?.GetJobParams(isNew);
        }

        #region API-Surface

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            coin = poolConfig.Template.As<BitcoinTemplate>();
            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<BitcoinPoolConfigExtra>();
            extraPoolPaymentProcessingConfig = poolConfig.PaymentProcessing?.Extra?.SafeExtensionDataAs<BitcoinPoolPaymentProcessingConfigExtra>();

            if(extraPoolConfig?.MaxActiveJobs.HasValue == true)
                maxActiveJobs = extraPoolConfig.MaxActiveJobs.Value;

            hasLegacyDaemon = extraPoolConfig?.HasLegacyDaemon == true;

            base.Configure(poolConfig, clusterConfig);
        }

        public override object[] GetSubscriberData(StratumConnection worker)
        {
            Contract.RequiresNonNull(worker, nameof(worker));

            var context = worker.ContextAs<BitcoinWorkerContext>();

            // assign unique ExtraNonce1 to worker (miner)
            context.ExtraNonce1 = extraNonceProvider.Next();

            // setup response data
            var responseData = new object[]
            {
                context.ExtraNonce1,
                BitcoinConstants.ExtranoncePlaceHolderLength - ExtranonceBytes,
            };

            return responseData;
        }

        public override async ValueTask<Share> SubmitShareAsync(StratumConnection worker, object submission,
            double stratumDifficultyBase, CancellationToken ct)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.RequiresNonNull(submission, nameof(submission));

            logger.LogInvoke(new object[] { worker.ConnectionId });

            if(submission is not object[] submitParams)
                throw new StratumException(StratumError.Other, "invalid params");

            var context = worker.ContextAs<BitcoinWorkerContext>();

            // extract params
            var workerValue = (submitParams[0] as string)?.Trim();
            var jobId = submitParams[1] as string;
            var extraNonce2 = submitParams[2] as string;
            var nTime = submitParams[3] as string;
            var nonce = submitParams[4] as string;
            var versionBits = context.VersionRollingMask.HasValue ? submitParams[5] as string : null;

            if(string.IsNullOrEmpty(workerValue))
                throw new StratumException(StratumError.Other, "missing or invalid workername");

            BitcoinJob job;

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
            var (share, blockHex) = job.ProcessShare(worker, extraNonce2, nTime, nonce, versionBits);

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

                var acceptResponse = await SubmitBlockAsync(share, blockHex);

                // is it still a block candidate?
                share.IsBlockCandidate = acceptResponse.Accepted;

                if(share.IsBlockCandidate)
                {
                    logger.Info(() => $"Daemon accepted block {share.BlockHeight} [{share.BlockHash}] submitted by {minerName}");

                    OnBlockFound();

                    // persist the coinbase transaction-hash to allow the payment processor
                    // to verify later on that the pool has received the reward for the block
                    share.TransactionConfirmationData = acceptResponse.CoinbaseTx;
                }

                else
                {
                    // clear fields that no longer apply
                    share.TransactionConfirmationData = null;
                }
            }

            return share;
        }

        public double ShareMultiplier => coin.ShareMultiplier;

        #endregion // API-Surface
    }
}
