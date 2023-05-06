using System.Security.Cryptography;
using Autofac;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Stratum;
using Miningcore.Time;
using NLog;

namespace Miningcore.Blockchain.Pandanite;

public class PandaniteJobManager : PandaniteJobManagerBase<PandaniteJob>
{
    public PandaniteJobManager(
        IComponentContext ctx,
        IMasterClock clock,
        IMessageBus messageBus) :
        base(ctx, clock, messageBus)
    {

    }

    private PandaniteCoinTemplate coin;

    protected override void PostChainIdentifyConfigure()
    {
        base.PostChainIdentifyConfigure();
    }

    protected override async Task<(bool IsNew, bool Force)> UpdateJob(CancellationToken ct, bool forceUpdate, string via = null, string json = null)
    {
        try
        {
            if(forceUpdate)
                lastJobRebroadcast = clock.Now;

            var problem = await Node.GetMiningProblem();

            // may happen if daemon is currently not connected to peers
            if(!problem.success)
            {
                logger.Warn(() => $"Unable to update job");
                return (false, forceUpdate);
            }

            var job = currentJob;

            var isNew = job == null ||
                    (job.LastHash.AsString() != problem.data.lastHash ||
                        problem.data.chainLength > job.Id);

            if(isNew)
                messageBus.NotifyChainHeight(poolConfig.Id, problem.data.chainLength, poolConfig.Template);

            if(isNew || forceUpdate)
            {
                var txs = await Node.GetTransactions();
                if(!txs.success)
                {
                    logger.Warn(() => $"Unable to download transactions");
                    return (false, forceUpdate);
                }

                // https://github.com/Pandanite-crypto/Pandanite/blob/55645977b3c7d98ababa9d467304182ccd76cfd1/src/core/constants.hpp#L20
                const int maxTransactions = 25000;

                var transactions = txs.data.OrderByDescending(x => x.fee).Take(maxTransactions - 1).ToList();

                using (var sha256 = SHA256.Create())
                using (var stream = new MemoryStream())
                {
                    var reward = new Transaction
                    {
                        to = poolConfig.Address,
                        amount = problem.data.miningFee,
                        fee = 0,
                        timestamp = problem.data.lastTimestamp,
                        isTransactionFee = true
                    };

                    transactions.Add(reward);

                    var tree = new MerkleTree(transactions);
                    var timestamp = (ulong)DateTimeOffset.Now.ToUnixTimeSeconds();

                    stream.Write(tree.RootHash);
                    stream.Write(problem.data.lastHash.ToByteArray());
                    stream.Write(BitConverter.GetBytes(problem.data.challengeSize));
                    stream.Write(BitConverter.GetBytes(timestamp));
                    stream.Flush();
                    stream.Position = 0;

                    var nonce = sha256.ComputeHash(stream);

                    job = new PandaniteJob
                    {
                        Id = problem.data.chainLength + 1,
                        JobId = nonce.AsString(),
                        Timestamp = timestamp,
                        ChallengeSize = (int)problem.data.challengeSize,
                        LastHash = problem.data.lastHash.ToByteArray(),
                        RootHash = tree.RootHash,
                        Transactions = transactions,
                        Nonce = nonce,
                        Problem = problem.data
                    };
                }

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
                        logger.Info(() => $"Detected new block {problem.data.chainLength} [{via}]");
                    else
                        logger.Info(() => $"Detected new block {problem.data.chainLength}");

                    // update stats
                    BlockchainStats.LastNetworkBlockTime = clock.Now;
                    BlockchainStats.BlockHeight = problem.data.chainLength;
                    BlockchainStats.NetworkDifficulty = problem.data.challengeSize;  // TODO: whats the difference ?
                    BlockchainStats.NextNetworkTarget = job.Nonce.AsString();
                    BlockchainStats.NextNetworkBits = problem.data.challengeSize.ToString();
                }

                else
                {
                    if(via != null)
                        logger.Debug(() => $"Template update {problem.data.chainLength} [{via}]");
                    else
                        logger.Debug(() => $"Template update {problem.data.chainLength}");
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
        return new object[]
        {
            currentJob.Id,
            currentJob.Nonce,
            isNew
        };
    }


    #region API-Surface

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        coin = pc.Template.As<PandaniteCoinTemplate>();
        base.Configure(pc, cc);
    }

    public virtual object[] GetSubscriberData(StratumConnection worker)
    {
        Contract.RequiresNonNull(worker);

        var context = worker.ContextAs<PandaniteWorkerContext>();

        // TODO is this required? maybe...
        // assign unique ExtraNonce1 to worker (miner)
        //context.ExtraNonce1 = extraNonceProvider.Next();

        // setup response data
        /*var responseData = new object[]
        {
            context.ExtraNonce1,
            4, // extranonce length
        };*/

        return new object [0];
    }

    public virtual async ValueTask<Share> SubmitShareAsync(StratumConnection worker, object submission,
        CancellationToken ct)
    {
        Contract.RequiresNonNull(worker);
        Contract.RequiresNonNull(submission);

        if(submission is not object[] submitParams)
            throw new StratumException(StratumError.Other, "invalid params");

        var context = worker.ContextAs<PandaniteWorkerContext>();

        // extract params
        var workerValue = (submitParams[0] as string)?.Trim();
        var jobId = submitParams[1] as string;
        var nonce = submitParams[2] as string;

        if(string.IsNullOrEmpty(workerValue))
            throw new StratumException(StratumError.Other, "missing or invalid workername");

        PandaniteJob job;

        lock(jobLock)
        {
            job = validJobs.FirstOrDefault(x => x.JobId == jobId);
        }

        if(job == null) {
            Console.WriteLine("job id length = {0}, job id = {1}", jobId.Length, jobId);
            foreach (var valid in validJobs) {
                Console.WriteLine(valid.JobId);
            }
            throw new StratumException(StratumError.JobNotFound, "job not found");
        }

        // validate & process
        var (share, block) = job.ProcessShare(worker, nonce);
        var blockRewardTx = job.GetMiningFeeTransaction();

        // enrich share with common data
        share.PoolId = poolConfig.Id;
        share.IpAddress = worker.RemoteEndpoint.Address.ToString();
        share.Miner = context.Miner;
        share.Worker = context.Worker;
        share.UserAgent = context.UserAgent;
        share.Source = clusterConfig.ClusterName;
        share.Created = clock.Now;
        share.TransactionConfirmationData = blockRewardTx.CalculateContentHash().AsString();

        // if block candidate, submit & check if accepted by network
        if(share.IsBlockCandidate)
        {
            logger.Info(() => $"Submitting block {share.BlockHeight} [{share.BlockHash}]");

            var acceptResponse = await SubmitBlockAsync(Node, share, block, ct);

            // is it still a block candidate?
            share.IsBlockCandidate = acceptResponse.Accepted;

            if(share.IsBlockCandidate)
            {
                var fees = job.Transactions
                    .Where(x => !x.isTransactionFee)
                    .Select(x => (long)x.fee)
                    .Sum();

                share.BlockReward = (blockRewardTx.amount + (ulong)fees) / (decimal)10000;
                share.BlockRewardDouble = (blockRewardTx.amount + (ulong)fees) / (double)10000;

                logger.Info(() => $"Daemon accepted block {share.BlockHeight} [{share.BlockHash}] submitted by {context.Miner}");

                OnBlockFound();
            }

            else
            {
                // clear fields that no longer apply
                share.TransactionConfirmationData = null;
            }
        }

        return share;
    }

    public double ShareMultiplier => 1.0d;

    #endregion // API-Surface
}
