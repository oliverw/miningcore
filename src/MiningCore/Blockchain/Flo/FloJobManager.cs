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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.Bitcoin.DaemonResponses;
using MiningCore.Blockchain.Flo.Configuration;
using MiningCore.Configuration;
using MiningCore.Contracts;
using MiningCore.DaemonInterface;
using MiningCore.Extensions;
using MiningCore.Notifications;
using MiningCore.Stratum;
using MiningCore.Time;
using NBitcoin;
using NBitcoin.DataEncoders;
using NLog;

namespace MiningCore.Blockchain.Flo
{
    public class FloJobManager<TJob> : BitcoinJobManager<TJob, BlockTemplate>
        where TJob : FloJob, new()
    {
        public FloJobManager(
            IComponentContext ctx,
            NotificationService notificationService,
            IMasterClock clock,
            IExtraNonceProvider extraNonceProvider) : base(ctx, notificationService, clock, extraNonceProvider)
        {

        }

        protected override string LogCat => "Flo Job Manager";
        protected new readonly List<FloJob> validJobs = new List<FloJob>();
        protected new FloJob currentJob;
        protected string coinbaseFloData;

        
        private FloPoolConfigExtra floExtraPoolConfig;

        #region Overrides of BitcoinJobManager<TJob, BlockTemplate>

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            floExtraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<FloPoolConfigExtra>();

            coinbaseFloData = floExtraPoolConfig.FloData.Trim();

            base.Configure(poolConfig, clusterConfig);
        }
        
        protected override async Task<bool> UpdateJob(bool forceUpdate, string via = null)
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
                var isNew = job == null ||
                    (blockTemplate != null &&
                    job.BlockTemplate?.PreviousBlockhash != blockTemplate.PreviousBlockhash &&
                    blockTemplate.Height > job.BlockTemplate?.Height);

                if (isNew || forceUpdate)
                {
                    job = new FloJob();

                    job.Init(blockTemplate, NextJobId(),
                        poolConfig, clusterConfig, clock, poolAddressDestination, networkType, isPoS,
                        ShareMultiplier,
                        coinbaseHasher, headerHasher, blockHasher, coinbaseFloData);

                    if (isNew)
                    {
                        if(via != null)
                            logger.Info($"[{LogCat}] Detected new block {blockTemplate.Height} via {via}");

                        // update stats
                        BlockchainStats.LastNetworkBlockTime = clock.Now;
                        BlockchainStats.BlockHeight = blockTemplate.Height;
                        BlockchainStats.NetworkDifficulty = job.Difficulty;
                    }

                    lock (jobLock)
                    {
                        validJobs.Add(job);

                        // trim active jobs
                        while (validJobs.Count > maxActiveJobs)
                            validJobs.RemoveAt(0);
                    }

                    currentJob = job;
                }

                return isNew;
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Error during {nameof(UpdateJob)}");
            }

            return false;
        }


        #endregion

    }
}
