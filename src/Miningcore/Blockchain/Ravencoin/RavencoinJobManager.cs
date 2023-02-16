using Autofac;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Crypto;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Rpc;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json;
using NLog;

namespace Miningcore.Blockchain.Ravencoin;

public class RavencoinJobManager : BitcoinJobManagerBase<RavencoinJob>
{
    public RavencoinJobManager(
        IComponentContext ctx,
        IMasterClock clock,
        IMessageBus messageBus,
        IExtraNonceProvider extraNonceProvider) :
        base(ctx, clock, messageBus, extraNonceProvider)
    {
    }

    private RavencoinTemplate coin;

    private async Task<RpcResponse<BlockTemplate>> GetBlockTemplateAsync(CancellationToken ct)
    {
        var result = await rpc.ExecuteAsync<BlockTemplate>(logger,
            BitcoinCommands.GetBlockTemplate, ct, extraPoolConfig?.GBTArgs ?? (object) GetBlockTemplateParams());

        return result;
    }

    private RpcResponse<BlockTemplate> GetBlockTemplateFromJson(string json)
    {
        var result = JsonConvert.DeserializeObject<JsonRpcResponse>(json);

        return new RpcResponse<BlockTemplate>(result!.ResultAs<BlockTemplate>());
    }

    private static RavencoinJob CreateJob()
    {
        return new RavencoinJob();
    }

    private double ShareMultiplier => coin.ShareMultiplier;


    protected override void PostChainIdentifyConfigure()
    {
        base.PostChainIdentifyConfigure();

        if(poolConfig.EnableInternalStratum == false || coin.HeaderHasherValue is not IHashAlgorithmInit hashInit)
            return;

        if(!hashInit.DigestInit(poolConfig))
            logger.Error(() => $"{hashInit.GetType().Name} initialization failed");
    }

    protected override async Task PostStartInitAsync(CancellationToken ct)
    {
        if(poolConfig.EnableInternalStratum == true)
        {
            // make sure we have a current light cache
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            do
            {
                var blockTemplate = await GetBlockTemplateAsync(ct);

                if(blockTemplate?.Response != null)
                {
                    logger.Info(() => "Loading current light cache ...");

                    await coin.KawpowHasher.GetCacheAsync(logger, (int) blockTemplate.Response.Height);

                    logger.Info(() => "Loaded current light cache");
                    break;
                }

                logger.Info(() => "Waiting for first valid block template");
            } while(await timer.WaitForNextTickAsync(ct));
        }
        await base.PostStartInitAsync(ct);
    }

    protected override async Task<(bool IsNew, bool Force)> UpdateJob(CancellationToken ct, bool forceUpdate, string via = null, string json = null)
    {
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

                var blockHeight = blockTemplate?.Height ?? currentJob.BlockTemplate.Height;

                var kawpowHasher = await coin.KawpowHasher.GetCacheAsync(logger, (int) blockHeight);

                job.Init(blockTemplate, NextJobId(),
                    poolConfig, extraPoolConfig, clusterConfig, clock, poolAddressDestination, network, isPoS,
                    ShareMultiplier, coin.CoinbaseHasherValue, coin.HeaderHasherValue, kawpowHasher);

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
                        logger.Debug(() => $"Template update {blockTemplate?.Height} [{via}]");
                    else
                        logger.Debug(() => $"Template update {blockTemplate?.Height}");
                }

                currentJob = job;
            }

            return (isNew, forceUpdate);
        }

        catch(OperationCanceledException)
        {
            // ignored
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

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        coin = pc.Template.As<RavencoinTemplate>();
        extraPoolConfig = pc.Extra.SafeExtensionDataAs<BitcoinPoolConfigExtra>();
        extraPoolPaymentProcessingConfig = pc.PaymentProcessing?.Extra?.SafeExtensionDataAs<BitcoinPoolPaymentProcessingConfigExtra>();

        if(extraPoolConfig?.MaxActiveJobs.HasValue == true)
            maxActiveJobs = extraPoolConfig.MaxActiveJobs.Value;

        hasLegacyDaemon = extraPoolConfig?.HasLegacyDaemon == true;

        if(pc.EnableInternalStratum == true)
        {
            coin.KawpowHasher.Setup(3);
        }

        base.Configure(pc, cc);
    }

    public object[] GetSubscriberData(StratumConnection worker)
    {
        Contract.RequiresNonNull(worker);

        var context = worker.ContextAs<RavencoinWorkerContext>();

        // assign unique ExtraNonce1 to worker (miner)
        context.ExtraNonce1 = extraNonceProvider.Next();

        // setup response data
        var responseData = new object[]
        {
            context.ExtraNonce1,
        };

        return responseData;
    }

    public void PrepareWorkerJob(RavencoinWorkerJob workerJob, out string headerHash)
    {
        headerHash = null;

        var job = currentJob;

        if(job != null)
        {
            lock(job)
            {
                job.PrepareWorkerJob(workerJob, out headerHash);
            }
        }
    }

    public async ValueTask<Share> SubmitShareAsync(StratumConnection worker, object submission,
        CancellationToken ct)
    {
        Contract.RequiresNonNull(worker);
        Contract.RequiresNonNull(submission);

        if(submission is not object[] submitParams)
            throw new StratumException(StratumError.Other, "invalid params");

        var context = worker.ContextAs<RavencoinWorkerContext>();

        // extract params
        var workerValue = (submitParams[0] as string)?.Trim();
        var jobId = submitParams[1] as string;
        var nonce = (submitParams[2] as string)?.Substring(2);
        var headerHash = (submitParams[3] as string)?.Substring(2);
        var mixHash = (submitParams[4] as string)?.Substring(2);

        if(string.IsNullOrEmpty(workerValue))
            throw new StratumException(StratumError.Other, "missing or invalid workername");

        RavencoinWorkerJob job;

        lock(context)
        {
            if((job = context.FindJob(jobId)) == null)
                throw new StratumException(StratumError.MinusOne, "invalid jobid");
        }

        if(job == null)
            throw new StratumException(StratumError.JobNotFound, "job not found");


        // validate & process
        var (share, blockHex) = job.ProcessShare(logger, worker, nonce, headerHash, mixHash);

        // enrich share with common data
        share.PoolId = poolConfig.Id;
        share.IpAddress = worker.RemoteEndpoint.Address.ToString();
        share.Miner = context.Miner;
        share.Worker = context.Worker;
        share.UserAgent = context.UserAgent;
        share.Source = clusterConfig.ClusterName;
        share.Created = clock.Now;

        // if block candidate, submit & check if accepted by network
        if(share.IsBlockCandidate)
        {
            logger.Info(() => $"Submitting block {share.BlockHeight} [{share.BlockHash}]");

            var acceptResponse = await SubmitBlockAsync(share, blockHex, ct);

            // is it still a block candidate?
            share.IsBlockCandidate = acceptResponse.Accepted;

            if(share.IsBlockCandidate)
            {
                logger.Info(() => $"Daemon accepted block {share.BlockHeight} [{share.BlockHash}] submitted by {context.Miner}");

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

    #endregion // API-Surface
}
