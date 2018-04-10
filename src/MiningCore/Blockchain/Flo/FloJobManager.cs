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
using System.Threading.Tasks;
using Autofac;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.Bitcoin.Configuration;
using MiningCore.Blockchain.Bitcoin.DaemonResponses;
using MiningCore.Blockchain.Flo.Configuration;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Notifications;
using MiningCore.Time;
using NLog;

namespace MiningCore.Blockchain.Flo
{
    public class FloJobManager : BitcoinJobManager<FloJob, BlockTemplate>
    {
        public FloJobManager(
            IComponentContext ctx,
            NotificationService notificationService,
            IMasterClock clock,
            IExtraNonceProvider extraNonceProvider) :
            base(ctx, notificationService, clock, extraNonceProvider)
        {
        }

        protected FloPoolConfigExtra extraFloPoolConfig;
        
        #region Overrides

        protected override string LogCat => "Flo Job Manager";

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            extraFloPoolConfig = poolConfig.Extra.SafeExtensionDataAs<FloPoolConfigExtra>();

            base.Configure(poolConfig, clusterConfig);
        }

        protected override async Task<(bool IsNew, bool Force)> UpdateJob(bool forceUpdate, string via = null)
        {
            logger.LogInvoke(LogCat);

            try
            {
                var response = await GetBlockTemplateAsync();

                // may happen if daemon is currently not connected to peers
                if (response.Error != null)
                {
                    logger.Warn(() => $"[{LogCat}] Unable to update job. Daemon responded with: {response.Error.Message} Code {response.Error.Code}");
                    return (false, forceUpdate);
                }

                var blockTemplate = response.Response;

                var job = currentJob;
                var isNew = job == null ||
                    (blockTemplate != null &&
                    job.BlockTemplate?.PreviousBlockhash != blockTemplate.PreviousBlockhash &&
                    blockTemplate.Height > job.BlockTemplate?.Height);

                if (isNew || forceUpdate)
                {
                    job = new FloJob();

                    job.Init(blockTemplate, NextJobId(),
                        poolConfig, clusterConfig, clock, poolAddressDestination, networkType, isPoS,
                        ShareMultiplier, extraPoolPaymentProcessingConfig?.BlockrewardMultiplier ?? 1.0m,
                        coinbaseHasher, headerHasher, blockHasher, extraFloPoolConfig.FloData);

                    lock (jobLock)
                    {
                        if (isNew)
                        {
                            if (via != null)
                                logger.Info(() => $"[{LogCat}] Detected new block {blockTemplate.Height} via {via}");
                            else
                                logger.Info(() => $"[{LogCat}] Detected new block {blockTemplate.Height}");

                            validJobs.Clear();

                            // update stats
                            BlockchainStats.LastNetworkBlockTime = clock.Now;
                            BlockchainStats.BlockHeight = blockTemplate.Height;
                            BlockchainStats.NetworkDifficulty = job.Difficulty;
                        }

                        else
                        {
                            // trim active jobs
                            while (validJobs.Count > maxActiveJobs - 1)
                                validJobs.RemoveAt(0);
                        }

                        validJobs.Add(job);
                    }

                    currentJob = job;
                }

                return (isNew, forceUpdate);
            }

            catch (Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Error during {nameof(UpdateJob)}");
            }

            return (false, forceUpdate);
        }

        #endregion // Overrides
    }
}
