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

using Autofac;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.Bitcoin.DaemonResponses;
using MiningCore.Blockchain.UniversalCurrency.DaemonResponses;
using MiningCore.DaemonInterface;
using MiningCore.Extensions;
using MiningCore.Messaging;
using MiningCore.Time;
using MiningCore.Util;
using NBitcoin;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Net;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MiningCore.Blockchain.UniversalCurrency
{
    public class UniversalCurrencyJobManager : BitcoinJobManager<BitcoinJob<BlockTemplate>, BlockTemplate>
    {
        public UniversalCurrencyJobManager(
            IComponentContext ctx,
            IMasterClock clock,
            IMessageBus messageBus,
            IExtraNonceProvider extraNonceProvider) :
            base(ctx, clock, messageBus, extraNonceProvider)
        {
        }

        #region Overrides

        protected override string LogCat => "UniversalCurrency Job Manager";

        protected override async Task<bool> AreDaemonsHealthyLegacyAsync()
        {
            var responses = await daemon.ExecuteCmdAllAsync<UniversalCurrencyGetInfoResponse>(BitcoinCommands.GetInfo);

            if (responses.Where(x => x.Error?.InnerException?.GetType() == typeof(DaemonClientException))
                .Select(x => (DaemonClientException)x.Error.InnerException)
                .Any(x => x.Code == HttpStatusCode.Unauthorized))
                logger.ThrowLogPoolStartupException($"Daemon reports invalid credentials", LogCat);

            return responses.All(x => x.Error == null);
        }

        protected override async Task<bool> AreDaemonsConnectedLegacyAsync()
        {
            var response = await daemon.ExecuteCmdAnyAsync<UniversalCurrencyGetInfoResponse>(BitcoinCommands.GetInfo);

            return response.Error == null && response.Response.Connections > 0;
        }

        protected override async Task ShowDaemonSyncProgressLegacyAsync()
        {
            var infos = await daemon.ExecuteCmdAllAsync<UniversalCurrencyGetInfoResponse>(BitcoinCommands.GetInfo);

            if (infos.Length > 0)
            {
                var blockCount = infos
                    .Max(x => x.Response?.Blocks);

                if (blockCount.HasValue)
                {
                    // get list of peers and their highest block height to compare to ours
                    var peerInfo = await daemon.ExecuteCmdAnyAsync<PeerInfo[]>(BitcoinCommands.GetPeerInfo);
                    var peers = peerInfo.Response;

                    if (peers != null && peers.Length > 0)
                    {
                        var totalBlocks = peers.Max(x => x.StartingHeight);
                        var percent = totalBlocks > 0 ? (double)blockCount / totalBlocks * 100 : 0;
                        logger.Info(() => $"[{LogCat}] Daemons have downloaded {percent:0.00}% of blockchain from {peers.Length} peers");
                    }
                }
            }
        }

        protected override async Task PostStartInitAsync(CancellationToken ct)
        {
            var commands = new[]
            {
                new DaemonCmd(BitcoinCommands.ValidateAddress, new[] { poolConfig.Address }),
                new DaemonCmd(BitcoinCommands.SubmitBlock),
                new DaemonCmd(!hasLegacyDaemon ? BitcoinCommands.GetBlockchainInfo : BitcoinCommands.GetInfo),
                new DaemonCmd(BitcoinCommands.GetDifficulty),
            };

            var results = await daemon.ExecuteBatchAnyAsync(commands);

            if (results.Any(x => x.Error != null))
            {
                var resultList = results.ToList();
                var errors = results.Where(x => x.Error != null && commands[resultList.IndexOf(x)].Method != BitcoinCommands.SubmitBlock)
                    .ToArray();

                if (errors.Any())
                    logger.ThrowLogPoolStartupException($"Init RPC failed: {string.Join(", ", errors.Select(y => y.Error.Message))}", LogCat);
            }

            // extract results
            var validateAddressResponse = results[0].Response.ToObject<ValidateAddressResponse>();
            var submitBlockResponse = results[1];
            var blockchainInfoResponse = !hasLegacyDaemon ? results[2].Response.ToObject<BlockchainInfo>() : null;
            var daemonInfoResponse = hasLegacyDaemon ? results[2].Response.ToObject<UniversalCurrencyGetInfoResponse>() : null;
            var difficultyResponse = results[3].Response.ToObject<JToken>();

            // ensure pool owns wallet
            if (!validateAddressResponse.IsValid)
                logger.ThrowLogPoolStartupException($"Daemon reports pool-address '{poolConfig.Address}' as invalid", LogCat);

            if (clusterConfig.PaymentProcessing?.Enabled == true && !validateAddressResponse.IsMine)
                logger.ThrowLogPoolStartupException($"Daemon does not own pool-address '{poolConfig.Address}'", LogCat);

            isPoS = difficultyResponse.Values().Any(x => x.Path == "proof-of-stake");

            // Create pool address script from response
            if (!isPoS)
            {
                // bitcoincashd returns a different address than what was passed in
                if (!validateAddressResponse.Address.StartsWith("bitcoincash:"))
                    poolAddressDestination = AddressToDestination(validateAddressResponse.Address);
                else
                    poolAddressDestination = AddressToDestination(poolConfig.Address);
            }

            else
                poolAddressDestination = new PubKey(validateAddressResponse.PubKey);

            // chain detection
            if (!hasLegacyDaemon)
            {
                if (blockchainInfoResponse.Chain.ToLower() == "test")
                    networkType = BitcoinNetworkType.Test;
                else if (blockchainInfoResponse.Chain.ToLower() == "regtest")
                    networkType = BitcoinNetworkType.RegTest;
                else
                    networkType = BitcoinNetworkType.Main;
            }

            else
                networkType = daemonInfoResponse.Testnet ? BitcoinNetworkType.Test : BitcoinNetworkType.Main;

            if (clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
                ConfigureRewards();

            // update stats
            BlockchainStats.NetworkType = networkType.ToString();
            BlockchainStats.RewardType = isPoS ? "POS" : "POW";

            // block submission RPC method
            if (submitBlockResponse.Error?.Message?.ToLower() == "method not found")
                hasSubmitBlockMethod = false;
            else if (submitBlockResponse.Error?.Code == -1)
                hasSubmitBlockMethod = true;
            else
                logger.ThrowLogPoolStartupException($"Unable detect block submission RPC method", LogCat);

            if (!hasLegacyDaemon)
                await UpdateNetworkStatsAsync();
            else
                await UpdateNetworkStatsLegacyAsync();

            // Periodically update network stats
            Observable.Interval(TimeSpan.FromMinutes(10))
                .Select(via => Observable.FromAsync(() => !hasLegacyDaemon ? UpdateNetworkStatsAsync() : UpdateNetworkStatsLegacyAsync()))
                .Concat()
                .Subscribe();

            SetupCrypto();
            SetupJobUpdates();
        }

        private async Task UpdateNetworkStatsAsync()
        {
            logger.LogInvoke(LogCat);

            var results = await daemon.ExecuteBatchAnyAsync(
                new DaemonCmd(BitcoinCommands.GetBlockchainInfo),
                new DaemonCmd(BitcoinCommands.GetMiningInfo),
                new DaemonCmd(BitcoinCommands.GetNetworkInfo)
            );

            if (results.Any(x => x.Error != null))
            {
                var errors = results.Where(x => x.Error != null).ToArray();

                if (errors.Any())
                    logger.Warn(() => $"[{LogCat}] Error(s) refreshing network stats: {string.Join(", ", errors.Select(y => y.Error.Message))}");
            }

            var infoResponse = results[0].Response.ToObject<BlockchainInfo>();
            var miningInfoResponse = results[1].Response.ToObject<UniversalCurrencyGetMiningInfoResponse>();
            var networkInfoResponse = results[2].Response.ToObject<NetworkInfo>();

            BlockchainStats.BlockHeight = infoResponse.Blocks;
            BlockchainStats.NetworkHashrate = miningInfoResponse.NetworkHashps;
            BlockchainStats.ConnectedPeers = networkInfoResponse.Connections;
        }

        private async Task UpdateNetworkStatsLegacyAsync()
        {
            logger.LogInvoke(LogCat);

            var results = await daemon.ExecuteBatchAnyAsync(
                new DaemonCmd(BitcoinCommands.GetMiningInfo),
                new DaemonCmd(BitcoinCommands.GetConnectionCount)
            );

            if (results.Any(x => x.Error != null))
            {
                var errors = results.Where(x => x.Error != null).ToArray();

                if (errors.Any())
                    logger.Warn(() => $"[{LogCat}] Error(s) refreshing network stats: {string.Join(", ", errors.Select(y => y.Error.Message))}");
            }

            var miningInfoResponse = results[0].Response.ToObject<UniversalCurrencyGetMiningInfoResponse>();
            var connectionCountResponse = results[1].Response.ToObject<object>();

            BlockchainStats.BlockHeight = miningInfoResponse.Blocks;
            BlockchainStats.ConnectedPeers = (int)(long)connectionCountResponse;
        }

        #endregion // Overrides
    }
}
