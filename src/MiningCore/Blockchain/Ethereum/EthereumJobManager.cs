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
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Autofac;
using MiningCore.Blockchain.Ethereum.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.DaemonInterface;
using MiningCore.Extensions;
using MiningCore.Native;
using MiningCore.Stratum;
using MiningCore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
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

            using (var rng = RandomNumberGenerator.Create())
            {
                instanceId = new byte[EthereumConstants.InstanceIdSize];
                rng.GetNonZeroBytes(instanceId);
            }
        }

        private readonly byte[] instanceId;
        private DaemonEndpointConfig[] daemonEndpoints;
        private DaemonClient daemon;
        private EthereumNetworkType networkType;

        protected async Task<bool> UpdateJob()
        {
            try
            {
                var blockTemplate = await GetBlockTemplateAsync();

                // may happen if daemon is currently not connected to peers
                if (blockTemplate == null)
                    return false;

                lock (jobLock)
                {
                    var isNew = currentJob == null ||
                                currentJob.BlockTemplate.ParentHash != blockTemplate.ParentHash ||
                                currentJob.BlockTemplate.Height < blockTemplate.Height;

                    if (isNew)
                    {
                        currentJob = new EthereumJob(blockTemplate, instanceId, NextJobId(),
                            poolConfig, clusterConfig);

                        currentJob.Init();

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
                Header = work[0].HexToByteArray(),
                Seed = work[1].HexToByteArray(),
                Target = new BigInteger(work[2].HexToByteArray().ToReverseArray()), // BigInteger.Parse(work[2], NumberStyles.HexNumber | NumberStyles.AllowHexSpecifier)
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
            //var response = await daemon.ExecuteCmdAnyAsync<SubmitResponse>(EC.SubmitBlock, new[] {share.BlobHex});

            //if (response.Error != null || response?.Response?.Status != "OK")
            //{
            //    var error = response.Error?.Message ?? response.Response?.Status;

            //    logger.Warn(() => $"[{LogCat}] Block {share.BlockHeight} [{share.BlobHash.Substring(0, 6)}] submission failed with: {error}");
            //    return false;
            //}

            return true;
        }

        protected async Task UpdateNetworkStats()
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

            BlockchainStats.BlockHeight = block.Height.HasValue ? (long) block.Height.Value : -1;
            BlockchainStats.NetworkDifficulty = block.Difficulty.IntegralFromHex<ulong>();
            BlockchainStats.NetworkHashRate = 0;    // TODO
            BlockchainStats.ConnectedPeers = peerCount;
        }

        #region API-Surface

        public IObservable<Unit> Blocks { get; private set; }

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

            var addressBytes = address.HexToByteArray();

            if (addressBytes.Length != EthereumConstants.AddressLength)
                return false;

            return true;
        }

        public BlockchainStats BlockchainStats { get; } = new BlockchainStats();

        #endregion // API-Surface

        #region Overrides

        protected override string LogCat => "Monero Job Manager";

        protected override void ConfigureDaemons()
        {
            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            daemon = new DaemonClient(jsonSerializerSettings);
            daemon.Configure(daemonEndpoints);
        }

        protected override async Task<bool> IsDaemonHealthy()
        {
            var responses = await daemon.ExecuteCmdAllAsync<Block>(EC.GetBlockByNumber, new[] { (object) "pending", true });

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

                var isSynched = responses.All(x => x.Error == null);

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

            await UpdateNetworkStats();

            SetupJobUpdates();
        }

        protected virtual void SetupJobUpdates()
        {
            // periodically update block-template from daemon
            Blocks = Observable.Interval(TimeSpan.FromMilliseconds(poolConfig.BlockRefreshInterval))
                .Select(_ => Observable.FromAsync(UpdateJob))
                .Concat()
                .Do(isNew =>
                {
                    if (isNew)
                        logger.Info(() => $"[{LogCat}] New block detected");
                })
                .Where(isNew => isNew)
                .Select(_ => Unit.Default)
                .Publish()
                .RefCount();
        }

        #endregion // Overrides
    }
}
