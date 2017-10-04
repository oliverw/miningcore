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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Autofac;
using MiningCore.Blockchain.Ethereum.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.DaemonInterface;
using MiningCore.Extensions;
using MiningCore.Stratum;
using MiningCore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Block = MiningCore.Blockchain.Ethereum.DaemonResponses.Block;
using Contract = MiningCore.Contracts.Contract;
using EC = MiningCore.Blockchain.Ethereum.GethCommands;

namespace MiningCore.Blockchain.Ethereum
{
    public class EthereumJobManager : JobManagerBase<EthereumJob>
    {
        public EthereumJobManager(
            IComponentContext ctx) :
            base(ctx)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
        }

        private DaemonEndpointConfig[] daemonEndpoints;
        private DaemonClient daemon;
        private EthereumNetworkType networkType;
        private readonly EthereumExtraNonceProvider extraNonceProvider = new EthereumExtraNonceProvider();

        private const int MaxBlockBacklog = 3;
        protected readonly Dictionary<string, EthereumJob> validJobs = new Dictionary<string, EthereumJob>();

        protected async Task<bool> UpdateJob()
        {
            try
            {
                var blockTemplate = await GetBlockTemplateAsync();

                // may happen if daemon is currently not connected to peers
                if (blockTemplate == null || blockTemplate.Header.Length == 0)
                    return false;

                lock (jobLock)
                {
                    var isNew = currentJob == null ||
                                currentJob.BlockTemplate.ParentHash != blockTemplate.ParentHash ||
                                currentJob.BlockTemplate.Height < blockTemplate.Height ||
                                currentJob.BlockTemplate.Seed != blockTemplate.Seed;

                    if (isNew)
                    {
                        var jobId = NextJobId("x8");

                        // update template
                        currentJob = new EthereumJob(jobId, blockTemplate);

                        // add jobs
                        validJobs[jobId] = currentJob;

                        // remove old ones
                        var obsoleteKeys = validJobs.Keys
                            .Where(key=> validJobs[key].BlockTemplate.Height < currentJob.BlockTemplate.Height - MaxBlockBacklog).ToArray();

                        foreach (var key in obsoleteKeys)
                            validJobs.Remove(key);

                        // update stats
                        BlockchainStats.LastNetworkBlockTime = DateTime.UtcNow;
                    }

                    return isNew;
                }
            }

            catch (Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Error during {nameof(UpdateJob)}");
            }

            return false;
        }

        private async Task<EthereumBlockTemplate> GetBlockTemplateAsync()
        {
            var commands = new[]
            {
                new DaemonCmd(EC.GetBlockByNumber, new[] { (object) "pending", true }),
                new DaemonCmd(EC.GetWork),
            };

            var results = await daemon.ExecuteBatchAnyAsync(commands);

            if (results.Any(x => x.Error != null))
            {
                var errors = results.Where(x => x.Error != null)
                    .ToArray();

                if (errors.Any())
                { 
                    logger.Warn(() => $"[{LogCat}] Error(s) refreshing blocktemplate: {string.Join(", ", errors.Select(y => y.Error.Message))})");
                    return null;
                }
            }

            // extract results
            var block = results[0].Response.ToObject<Block>();
            var work = results[1].Response.ToObject<string[]>();

            var result = new EthereumBlockTemplate
            {
                Header = work[0],
                Seed = work[1],
                Target = BigInteger.Parse("0" + work[2].Substring(2), NumberStyles.HexNumber),
                Difficulty = block.Difficulty.IntegralFromHex<ulong>(),
                Height = block.Height.Value,
                ParentHash = block.ParentHash,
            };

            return result;
        }

        private async Task ShowDaemonSyncProgressAsync()
        {
            var infos = await daemon.ExecuteCmdAllAsync<JToken>(EC.GetSyncState);
            var firstValidResponse = infos.FirstOrDefault(x => x.Error == null && x.Response != null)?.Response;

            if (firstValidResponse != null)
            {
                // eth_syncing returns false if not synching 
                if (firstValidResponse.Type == JTokenType.Boolean)
                    return;

                var syncStates = infos.Where(x => x.Error == null && x.Response != null && firstValidResponse.Type == JTokenType.Object)
                    .Select(x => x.Response.ToObject<SyncState>())
                    .ToArray();

                var lowestHeight = syncStates.Min(x => x.CurrentBlock);
                var totalBlocks = syncStates.Max(x => x.HighestBlock);
                var percent = (double)lowestHeight / totalBlocks * 100;

                // get peer count
                var response = await daemon.ExecuteCmdAllAsync<string>(EC.GetPeerCount);
                var peerCount = response.Where(x=> x.Error == null && x.Response != null).Max(x=> x.Response.IntegralFromHex<uint>());

                logger.Info(() => $"[{LogCat}] Daemons have downloaded {percent:0.00}% of blockchain from {peerCount} peers");
            }
        }

        private async Task<bool> SubmitBlockAsync(EthereumShare share)
        {
            // submit work
            var response = await daemon.ExecuteCmdAnyAsync<object>(EC.SubmitWork, new[]
            {
                share.FullNonceHex,
                share.HeaderHash,
                share.MixHash
            });

            if (response.Error != null || (bool?) response.Response == false)
            {
                var error = response.Error?.Message ?? response?.Response?.ToString();

                logger.Warn(() => $"[{LogCat}] Block {share.BlockHeight} submission failed with: {error}");
                return false;
            }

            return true;
        }

        private object[] GetJobParamsForStratum(bool isNew)
        {
            lock (jobLock)
            {
                return new object[]
                {
                    currentJob.Id,
                    currentJob.BlockTemplate.Seed,
                    currentJob.BlockTemplate.Header,
                    isNew
                };
            }
        }

        #region API-Surface

        public IObservable<object> Jobs { get; private set; }

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            // extract standard daemon endpoints
            daemonEndpoints = poolConfig.Daemons
                .Where(x => string.IsNullOrEmpty(x.Category))
                .ToArray();

            base.Configure(poolConfig, clusterConfig);
        }

        public bool ValidateAddress(string address)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

            if (EthereumConstants.ZeroHashPattern.IsMatch(address) ||
                !EthereumConstants.ValidAddressPattern.IsMatch(address))
                return false;

            return true;
        }

        public void PrepareWorker(StratumClient<EthereumWorkerContext> client)
        {
            client.Context.ExtraNonce1 = extraNonceProvider.Next();
        }

        public async Task<IShare> SubmitShareAsync(StratumClient<EthereumWorkerContext> worker,
            string[] request, double stratumDifficulty, double stratumDifficultyBase)
        {
            // var miner = request[0];
            var jobId = request[1];
            var nonce = request[2];
            EthereumJob job;

            // stale?
            lock (jobLock)
            {
                if(!validJobs.TryGetValue(jobId, out job))
                    throw new StratumException(StratumError.MinusOne, "stale share");
            }

            // validate & process
            var share = await job.ProcessShareAsync(worker, nonce);

            // if block candidate, submit & check if accepted by network
            if (share.IsBlockCandidate)
            {
                logger.Info(() => $"[{LogCat}] Submitting block {share.BlockHeight}");

                share.IsBlockCandidate = await SubmitBlockAsync(share);

                if (share.IsBlockCandidate)
                {
                    logger.Info(() => $"[{LogCat}] Daemon accepted block {share.BlockHeight}");
                }
            }

            // enrich share with common data
            share.PoolId = poolConfig.Id;
            share.NetworkDifficulty = BlockchainStats.NetworkDifficulty;
            share.StratumDifficulty = stratumDifficulty;
            share.StratumDifficultyBase = stratumDifficultyBase;
            share.Created = DateTime.UtcNow;

            return share;
        }

        public async Task UpdateNetworkStatsAsync()
        {
            var commands = new[]
            {
                new DaemonCmd(EC.GetBlockByNumber, new[] { (object) "latest", true }),
                new DaemonCmd(EC.GetPeerCount),
            };

            var results = await daemon.ExecuteBatchAnyAsync(commands);

            if (results.Any(x => x.Error != null))
            {
                var errors = results.Where(x => x.Error != null)
                    .ToArray();

                if (errors.Any())
                    logger.Warn(() => $"[{LogCat}] Error(s) refreshing network stats: {string.Join(", ", errors.Select(y => y.Error.Message))})");
            }

            // extract results
            var block = results[0].Response.ToObject<Block>();
            var peerCount = results[1].Response.ToObject<string>().IntegralFromHex<int>();

            BlockchainStats.BlockHeight = block.Height.HasValue ? (long)block.Height.Value : -1;
            BlockchainStats.NetworkDifficulty = block.Difficulty.IntegralFromHex<ulong>();
            BlockchainStats.NetworkHashRate = 0;    // TODO
            BlockchainStats.ConnectedPeers = peerCount;
        }

        public BlockchainStats BlockchainStats { get; } = new BlockchainStats();

        #endregion // API-Surface

        #region Overrides

        protected override string LogCat => "Ethereum Job Manager";

        protected override void ConfigureDaemons()
        {
            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            daemon = new DaemonClient(jsonSerializerSettings);
            daemon.Configure(daemonEndpoints);
        }

        protected override async Task<bool> IsDaemonHealthy()
        {
            var responses = await daemon.ExecuteCmdAllAsync<Block>(EC.GetBlockByNumber, new[] { (object) "pending", true });

            if (responses.Where(x => x.Error?.InnerException?.GetType() == typeof(DaemonClientException))
                .Select(x => (DaemonClientException)x.Error.InnerException)
                .Any(x => x.Code == HttpStatusCode.Unauthorized))
                logger.ThrowLogPoolStartupException($"Daemon reports invalid credentials", LogCat);

            return responses.All(x => x.Error == null);
        }

        protected override async Task<bool> IsDaemonConnected()
        {
            var response = await daemon.ExecuteCmdAnyAsync<string>(EC.GetPeerCount);

            return response.Error == null && response.Response.IntegralFromHex<uint>() > 0;
        }

        protected override async Task EnsureDaemonsSynchedAsync()
        {
            var syncPendingNotificationShown = false;

            while (true)
            {
                var responses = await daemon.ExecuteCmdAllAsync<string[]>(EC.GetWork);

                var isSynched = responses.All(x => x.Error == null &&
                    !x.Response.Any(string.IsNullOrEmpty));

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
            var commands = new[]
            {
                new DaemonCmd(EC.GetNetVersion),
                new DaemonCmd(EC.GetAccounts),
            };

            var results = await daemon.ExecuteBatchAnyAsync(commands);

            if (results.Any(x => x.Error != null))
            {
                var errors = results.Where(x => x.Error != null)
                    .ToArray();

                if (errors.Any())
                    logger.ThrowLogPoolStartupException($"Init RPC failed: {string.Join(", ", errors.Select(y => y.Error.Message))}", LogCat);
            }

            // extract results
            var netVersionResponse = results[0].Response.ToObject<string>();
            var accountsResponse = results[1].Response.ToObject<string[]>();

            // ensure pool owns wallet
            if (!accountsResponse.Contains(poolConfig.Address))
                logger.ThrowLogPoolStartupException($"Wallet-Daemon does not own pool-address '{poolConfig.Address}'", LogCat);

            // chain detection
            int netWorkTypeInt = 0;
            if (int.TryParse(netVersionResponse, out netWorkTypeInt))
            {
                networkType = (EthereumNetworkType) netWorkTypeInt;

                if(!Enum.IsDefined(typeof(EthereumNetworkType), networkType))
                    networkType = EthereumNetworkType.Unknown;
            }

            else
                networkType = EthereumNetworkType.Unknown;

            // update stats
            BlockchainStats.RewardType = "POW";
            BlockchainStats.NetworkType = networkType.ToString();

            await UpdateNetworkStatsAsync();

            SetupJobUpdates();
        }

        protected virtual void SetupJobUpdates()
        {
            // periodically update block-template from daemon
            Jobs = Observable.Interval(TimeSpan.FromMilliseconds(poolConfig.BlockRefreshInterval))
                .Select(_ => Observable.FromAsync(UpdateJob))
                .Concat()
                .Do(isNew =>
                {
                    if (isNew)
                        logger.Info(() => $"[{LogCat}] New block detected");
                })
                .Where(isNew => isNew)
                .Select(_ => GetJobParamsForStratum(true))
                .Publish()
                .RefCount();
        }

        #endregion // Overrides
    }
}
