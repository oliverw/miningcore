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
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningCore.Blockchain.Monero.Configuration;
using MiningCore.Blockchain.Monero.DaemonRequests;
using MiningCore.Blockchain.Monero.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.DaemonInterface;
using MiningCore.Extensions;
using MiningCore.Notifications;
using MiningCore.Payments;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using MiningCore.Time;
using MiningCore.Util;
using Newtonsoft.Json;
using Contract = MiningCore.Contracts.Contract;
using MC = MiningCore.Blockchain.Monero.MoneroCommands;
using MWC = MiningCore.Blockchain.Monero.MoneroWalletCommands;

namespace MiningCore.Blockchain.Monero
{
    [CoinMetadata(CoinType.XMR, CoinType.AEON, CoinType.ETN)]
    public class MoneroPayoutHandler : PayoutHandlerBase,
        IPayoutHandler
    {
        public MoneroPayoutHandler(
            IComponentContext ctx,
            IConnectionFactory cf,
            IMapper mapper,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo,
            IMasterClock clock,
            NotificationService notificationService) :
            base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, notificationService)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Contract.RequiresNonNull(paymentRepo, nameof(paymentRepo));

            this.ctx = ctx;
        }

        private readonly IComponentContext ctx;
        private DaemonClient daemon;
        private DaemonClient walletDaemon;
        private MoneroNetworkType? networkType;
        private MoneroPoolPaymentProcessingConfigExtra extraConfig;
        private bool walletSupportsTransferSplit;

        protected override string LogCategory => "Monero Payout Handler";

        private void HandleTransferResponse(DaemonResponse<TransferResponse> response, params Balance[] balances)
        {
            if (response.Error == null)
            {
                var txHash = response.Response.TxHash;
                var txFee = (decimal) response.Response.Fee / MoneroConstants.SmallestUnit[poolConfig.Coin.Type];

                logger.Info(() => $"[{LogCategory}] Payout transaction id: {txHash}, TxFee was {FormatAmount(txFee)}");

                PersistPayments(balances, txHash);
                NotifyPayoutSuccess(poolConfig.Id, balances, new[] { txHash }, txFee);
            }

            else
            {
                logger.Error(() => $"[{LogCategory}] Daemon command '{MWC.Transfer}' returned error: {response.Error.Message} code {response.Error.Code}");

                NotifyPayoutFailure(poolConfig.Id, balances, $"Daemon command '{MWC.Transfer}' returned error: {response.Error.Message} code {response.Error.Code}", null);
            }
        }

        private void HandleTransferResponse(DaemonResponse<TransferSplitResponse> response, params Balance[] balances)
        {
            if (response.Error == null)
            {
                var txHashes = response.Response.TxHashList;
                var txFees = response.Response.FeeList.Select(x => (decimal) x / MoneroConstants.SmallestUnit[poolConfig.Coin.Type]).ToArray();

                logger.Info(() => $"[{LogCategory}] Split-Payout transaction ids: {string.Join(", ", txHashes)}, Corresponding TxFees were {string.Join(", ", txFees.Select(FormatAmount))}");

                PersistPayments(balances, txHashes.First());
                NotifyPayoutSuccess(poolConfig.Id, balances, txHashes, txFees.Sum());
            }

            else
            {
                logger.Error(() => $"[{LogCategory}] Daemon command '{MWC.TransferSplit}' returned error: {response.Error.Message} code {response.Error.Code}");

                NotifyPayoutFailure(poolConfig.Id, balances, $"Daemon command '{MWC.TransferSplit}' returned error: {response.Error.Message} code {response.Error.Code}", null);
            }
        }

        private async Task<MoneroNetworkType> GetNetworkTypeAsync()
        {
            if (!networkType.HasValue)
            {
                var infoResponse = await daemon.ExecuteCmdAnyAsync(MC.GetInfo);
                var info = infoResponse.Response.ToObject<GetInfoResponse>();

                networkType = info.IsTestnet ? MoneroNetworkType.Test : MoneroNetworkType.Main;
            }

            return networkType.Value;
        }

        private async Task PayoutBatch(Balance[] balances)
        {
            // build request
            var request = new TransferRequest
            {
                Destinations = balances
                    .Where(x => x.Amount > 0)
                    .Select(x => new TransferDestination
                    {
                        Address = x.Address,
                        Amount = (ulong) Math.Floor(x.Amount * MoneroConstants.SmallestUnit[poolConfig.Coin.Type])
                    }).ToArray(),

                GetTxKey = true
            };

            if (request.Destinations.Length == 0)
                return;

            logger.Info(() => $"[{LogCategory}] Paying out {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

            // send command
            var transferResponse = await walletDaemon.ExecuteCmdSingleAsync<TransferResponse>(MWC.Transfer, request);

            // gracefully handle error -4 (transaction would be too large. try /transfer_split)
            if (transferResponse.Error?.Code == -4)
            {
                if (walletSupportsTransferSplit)
                {
                    logger.Info(() => $"[{LogCategory}] Retrying transfer using {MWC.TransferSplit}");

                    var transferSplitResponse = await walletDaemon.ExecuteCmdSingleAsync<TransferSplitResponse>(MWC.TransferSplit, request);

                    // gracefully handle error -4 (transaction would be too large. try /transfer_split)
                    if (transferResponse.Error?.Code != -4)
                    {
                        HandleTransferResponse(transferSplitResponse, balances);
                        return;
                    }
                }

                // retry paged
                logger.Info(() => $"[{LogCategory}] Retrying paged");

                var validBalances = balances.Where(x => x.Amount > 0).ToArray();
                var pageSize = 10;
                var pageCount = (int)Math.Ceiling((double)validBalances.Length / pageSize);

                for (var i = 0; i < pageCount; i++)
                {
                    var page = validBalances
                        .Skip(i * pageSize)
                        .Take(pageSize)
                        .ToArray();

                    // update request
                    request.Destinations = page
                        .Where(x => x.Amount > 0)
                        .Select(x => new TransferDestination
                        {
                            Address = x.Address,
                            Amount = (ulong)Math.Floor(x.Amount * MoneroConstants.SmallestUnit[poolConfig.Coin.Type])
                        }).ToArray();

                    logger.Info(() => $"[{LogCategory}] Page {i + 1}: Paying out {FormatAmount(page.Sum(x => x.Amount))} to {page.Length} addresses");

                    transferResponse = await walletDaemon.ExecuteCmdSingleAsync<TransferResponse>(MWC.Transfer, request);
                    HandleTransferResponse(transferResponse, page);

                    if (transferResponse.Error != null)
                        break;
                }
            }

            else
                HandleTransferResponse(transferResponse, balances);
        }

        private async Task PayoutToPaymentId(Balance balance)
        {
            // extract paymentId
            var address = (string) null;
            var paymentId = (string) null;

            var index = balance.Address.IndexOf(PayoutConstants.PayoutInfoSeperator);
            if (index != -1)
            {
                paymentId = balance.Address.Substring(index + 1);
                address = balance.Address.Substring(0, index);
            }

            if (string.IsNullOrEmpty(paymentId))
                throw new InvalidOperationException("invalid paymentid");

            // build request
            var request = new TransferRequest
            {
                Destinations = new[]
                {
                    new TransferDestination
                    {
                        Address = address,
                        Amount = (ulong) Math.Floor(balance.Amount * MoneroConstants.SmallestUnit[poolConfig.Coin.Type])
                    }
                },
                PaymentId = paymentId,
                GetTxKey = true
            };

            logger.Info(() => $"[{LogCategory}] Paying out {FormatAmount(balance.Amount)} with paymentId {paymentId}");

            // send command
            var result = await walletDaemon.ExecuteCmdSingleAsync<TransferResponse>(MWC.Transfer, request);

            if (walletSupportsTransferSplit)
            {
                // gracefully handle error -4 (transaction would be too large. try /transfer_split)
                if (result.Error?.Code == -4)
                {
                    logger.Info(() => $"[{LogCategory}] Retrying transfer using {MWC.TransferSplit}");

                    result = await walletDaemon.ExecuteCmdSingleAsync<TransferResponse>(MWC.TransferSplit, request);
                }
            }

            HandleTransferResponse(result, balance);
        }

        #region IPayoutHandler

        public async Task ConfigureAsync(ClusterConfig clusterConfig, PoolConfig poolConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;
            extraConfig = poolConfig.PaymentProcessing.Extra.SafeExtensionDataAs<MoneroPoolPaymentProcessingConfigExtra>();

            logger = LogUtil.GetPoolScopedLogger(typeof(MoneroPayoutHandler), poolConfig);

            // configure standard daemon
            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            var daemonEndpoints = poolConfig.Daemons
                .Where(x => string.IsNullOrEmpty(x.Category))
                .ToArray();

            daemon = new DaemonClient(jsonSerializerSettings);
            daemon.Configure(daemonEndpoints, MoneroConstants.DaemonRpcLocation);

            // configure wallet daemon
            var walletDaemonEndpoints = poolConfig.Daemons
                .Where(x => x.Category?.ToLower() == MoneroConstants.WalletDaemonCategory)
                .ToArray();

            walletDaemon = new DaemonClient(jsonSerializerSettings);
            walletDaemon.Configure(walletDaemonEndpoints, MoneroConstants.DaemonRpcLocation);

            // detect transfer_split support
            var response = await walletDaemon.ExecuteCmdSingleAsync<TransferResponse>(MWC.TransferSplit);
            walletSupportsTransferSplit = response.Error.Code != MoneroConstants.MoneroRpcMethodNotFound;
        }

        public async Task<Block[]> ClassifyBlocksAsync(Block[] blocks)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(blocks, nameof(blocks));

            var pageSize = 100;
            var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
            var result = new List<Block>();

            for(var i = 0; i < pageCount; i++)
            {
                // get a page full of blocks
                var page = blocks
                    .Skip(i * pageSize)
                    .Take(pageSize)
                    .ToArray();

                // NOTE: monerod does not support batch-requests
                for(var j = 0; j < page.Length; j++)
                {
                    var block = page[j];

                    var rpcResult = await daemon.ExecuteCmdAnyAsync<GetBlockHeaderResponse>(
                        MC.GetBlockHeaderByHeight,
                        new GetBlockHeaderByHeightRequest
                        {
                            Height = block.BlockHeight
                        });

                    if (rpcResult.Error != null)
                    {
                        logger.Debug(() => $"[{LogCategory}] Daemon reports error '{rpcResult.Error.Message}' (Code {rpcResult.Error.Code}) for block {block.BlockHeight}");
                        continue;
                    }

                    if (rpcResult.Response?.BlockHeader == null)
                    {
                        logger.Debug(() => $"[{LogCategory}] Daemon returned no header for block {block.BlockHeight}");
                        continue;
                    }

                    var blockHeader = rpcResult.Response.BlockHeader;

                    // update progress
                    block.ConfirmationProgress = Math.Min(1.0d, (double) blockHeader.Depth / MoneroConstants.PayoutMinBlockConfirmations);
                    result.Add(block);

                    // orphaned?
                    if (blockHeader.IsOrphaned || blockHeader.Hash != block.TransactionConfirmationData)
                    {
                        block.Status = BlockStatus.Orphaned;
                        block.Reward = 0;
                        continue;
                    }

                    // matured and spendable?
                    if (blockHeader.Depth >= MoneroConstants.PayoutMinBlockConfirmations)
                    {
                        block.Status = BlockStatus.Confirmed;
                        block.ConfirmationProgress = 1;
                        block.Reward = (decimal) blockHeader.Reward / MoneroConstants.SmallestUnit[poolConfig.Coin.Type];

                        logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");
                    }
                }
            }

            return result.ToArray();
        }

        public Task CalculateBlockEffortAsync(Block block, ulong accumulatedBlockShareDiff)
        {
            block.Effort = (double) accumulatedBlockShareDiff / block.NetworkDifficulty;

            return Task.FromResult(true);
        }

        public Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, Block block, PoolConfig pool)
        {
            var blockRewardRemaining = block.Reward;

            // Distribute funds to configured reward recipients
            foreach(var recipient in poolConfig.RewardRecipients.Where(x => x.Percentage > 0))
            {
                var amount = block.Reward * (recipient.Percentage / 100.0m);
                var address = recipient.Address;

                blockRewardRemaining -= amount;

                // skip transfers from pool wallet to pool wallet
                if (address != poolConfig.Address)
                {
                    logger.Info(() => $"Adding {FormatAmount(amount)} to balance of {address}");
                    balanceRepo.AddAmount(con, tx, poolConfig.Id, poolConfig.Coin.Type, address, amount);
                }
            }

            // Deduct static reserve for tx fees
            blockRewardRemaining -= MoneroConstants.StaticTransactionFeeReserve;

            return Task.FromResult(blockRewardRemaining);
        }

        public async Task PayoutAsync(Balance[] balances)
        {
            Contract.RequiresNonNull(balances, nameof(balances));

            // ensure we have peers
            var infoResponse = await daemon.ExecuteCmdAnyAsync<GetInfoResponse>(MC.GetInfo);
            if (infoResponse.Error != null || infoResponse.Response == null ||
                infoResponse.Response.IncomingConnectionsCount + infoResponse.Response.OutgoingConnectionsCount < 3)
            {
#if !DEBUG
                logger.Warn(() => $"[{LogCategory}] Payout aborted. Not enough peers (4 required)");
                return;
#endif
            }

            // simple balances first
            var simpleBalances = balances
                .Where(x => !x.Address.Contains(PayoutConstants.PayoutInfoSeperator))
                .ToArray();

            if (simpleBalances.Length > 0)
                await PayoutBatch(simpleBalances);

            // balances with paymentIds
            var minimumPaymentToPaymentId = extraConfig?.MinimumPaymentToPaymentId ?? poolConfig.PaymentProcessing.MinimumPayment;

            var paymentIdBalances = balances.Except(simpleBalances)
                .Where(x => x.Address.Contains(PayoutConstants.PayoutInfoSeperator) && x.Amount >= minimumPaymentToPaymentId)
                .ToArray();

            foreach(var balance in paymentIdBalances)
                await PayoutToPaymentId(balance);
        }

        #endregion // IPayoutHandler
    }
}
