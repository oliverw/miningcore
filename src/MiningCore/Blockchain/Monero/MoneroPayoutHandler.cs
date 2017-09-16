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
using Autofac.Features.Metadata;
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
using MiningCore.Util;
using Newtonsoft.Json;
using Contract = MiningCore.Contracts.Contract;
using MC = MiningCore.Blockchain.Monero.MoneroCommands;
using MWC = MiningCore.Blockchain.Monero.MoneroWalletCommands;

namespace MiningCore.Blockchain.Monero
{
    [CoinMetadata(CoinType.XMR)]
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
            IEnumerable<Meta<INotificationSender, NotificationSenderMetadataAttribute>> notificationSenders) :
            base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, notificationSenders)
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

        protected override string LogCategory => "Monero Payout Handler";

        private async Task HandleTransferResponseAsync(DaemonResponse<TransferResponse> response, params Balance[] balances)
        {
            if (response.Error == null)
            {
                var txHash = response.Response.TxHash;
                var txFee = (decimal) response.Response.Fee / MoneroConstants.Piconero;

                // check result
                if (string.IsNullOrEmpty(txHash))
                    logger.Error(() => $"[{LogCategory}] Daemon command '{MWC.Transfer}' did not return a transaction id!");
                else
                    logger.Info(() => $"[{LogCategory}] Payout transaction id: {txHash}, TxFee was {FormatAmount(txFee)}");

                PersistPayments(balances, txHash);

                await NotifyPayoutSuccess(balances, txHash, txFee);
            }

            else
            {
                logger.Error(() => $"[{LogCategory}] Daemon command '{MWC.Transfer}' returned error: {response.Error.Message} code {response.Error.Code}");

                await NotifyPayoutFailureAsync(balances, $"Daemon command '{MWC.Transfer}' returned error: {response.Error.Message} code {response.Error.Code}", null);
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
                        Amount = (ulong)Math.Floor(x.Amount * MoneroConstants.Piconero)
                    }).ToArray(),

                GetTxKey = true
            };

            if (request.Destinations.Length == 0)
                return;

            logger.Info(() => $"[{LogCategory}] Paying out {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

            // send command
            var result = await walletDaemon.ExecuteCmdAnyAsync<TransferResponse>(MWC.Transfer, request);

            // gracefully handle error -4 (transaction would be too large. try /transfer_split)
            if (result.Error?.Code == -4)
            {
                logger.Info(() => $"[{LogCategory}] Retrying transfer using {MWC.TransferSplit}");

                result = await walletDaemon.ExecuteCmdAnyAsync<TransferResponse>(MWC.TransferSplit, request);
            }

            await HandleTransferResponseAsync(result, balances);
        }

        private async Task PayoutToPaymentId(Balance balance)
        {
            // extract paymentId
            var address = (string)null;
            var paymentId = (string)null;

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
                Destinations = new []
                {
                    new TransferDestination
                    {
                        Address = address,
                        Amount = (ulong)Math.Floor(balance.Amount * MoneroConstants.Piconero)
                    }
                },
                PaymentId = paymentId,
                GetTxKey = true
            };

            logger.Info(() => $"[{LogCategory}] Paying out {FormatAmount(balance.Amount)} with paymentId {paymentId}");

            // send command
            var result = await walletDaemon.ExecuteCmdAnyAsync<TransferResponse>(MWC.Transfer, request);

            // gracefully handle error -4 (transaction would be too large. try /transfer_split)
            if (result.Error?.Code == -4)
            {
                logger.Info(() => $"[{LogCategory}] Retrying transfer using {MWC.TransferSplit}");

                result = await walletDaemon.ExecuteCmdAnyAsync<TransferResponse>(MWC.TransferSplit, request);
            }

            await HandleTransferResponseAsync(result, balance);
        }

        #region IPayoutHandler

        public void Configure(ClusterConfig clusterConfig, PoolConfig poolConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;

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
        }

        public async Task<Block[]> ClassifyBlocksAsync(Block[] blocks)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(blocks, nameof(blocks));

            var pageSize = 100;
            var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
            var result = new List<Block>();

            for (var i = 0; i < pageCount; i++)
            {
                // get a page full of blocks
                var page = blocks
                    .Skip(i * pageSize)
                    .Take(pageSize)
                    .ToArray();

                // NOTE: monerod does not support batch-requests
                for (var j = 0; j < page.Length; j++)
                {
                    var block = page[j];

                    var rpcResult = await daemon.ExecuteCmdAnyAsync<GetBlockHeaderResponse>(
                        MC.GetBlockHeaderByHeight,
                        new GetBlockHeaderByHeightRequest
                        {
                            Height = block.Blockheight
                        });

                    if (rpcResult.Error != null)
                    {
                        logger.Debug(() => $"[{LogCategory}] Daemon reports error '{rpcResult.Error.Message}' (Code {rpcResult.Error.Code}) for block {block.Blockheight}");
                        continue;
                    }

                    if (rpcResult.Response?.BlockHeader == null)
                    {
                        logger.Debug(() => $"[{LogCategory}] Daemon returned no header for block {block.Blockheight}");
                        continue;
                    }

                    var blockHeader = rpcResult.Response.BlockHeader;

                    // orphaned?
                    if (blockHeader.IsOrphaned || blockHeader.Hash != block.TransactionConfirmationData)
                    {
                        block.Status = BlockStatus.Orphaned;
                        result.Add(block);
                        continue;
                    }

                    // confirmed?
                    if (blockHeader.Depth < MoneroConstants.PayoutMinBlockConfirmations)
                        continue;

                    // matured and spendable 
                    block.Status = BlockStatus.Confirmed;
                    block.Reward = (decimal) blockHeader.Reward / MoneroConstants.Piconero;
                    result.Add(block);
                }
            }

            return result.ToArray();
        }

        public async Task UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, Block block,
            PoolConfig pool)
        {
            var blockRewardRemaining = block.Reward;

            // Distribute funds to configured reward recipients
            foreach (var recipient in poolConfig.RewardRecipients.Where(x=> x.Percentage > 0))
            {
                var amount = block.Reward * (recipient.Percentage / 100.0m);
                var address = recipient.Address;

                blockRewardRemaining -= amount;

                logger.Info(() => $"Adding {FormatAmount(amount)} to balance of {address}");
                balanceRepo.AddAmount(con, tx, poolConfig.Id, poolConfig.Coin.Type, address, amount);
            }

            // Tiny donation to MiningCore developer(s)
            if (!clusterConfig.DisableDevDonation &&
                await GetNetworkTypeAsync() == MoneroNetworkType.Main)
            {
                var amount = block.Reward * MoneroConstants.DevReward;
                var address = MoneroConstants.DevAddress;

                blockRewardRemaining -= amount;

                logger.Info(() => $"Adding {FormatAmount(amount)} to balance of {address}");
                balanceRepo.AddAmount(con, tx, poolConfig.Id, poolConfig.Coin.Type, address, amount);
            }

            // Deduct static reserve for tx fees
            blockRewardRemaining -= MoneroConstants.StaticTransactionFeeReserve;

            // update block-reward
            block.Reward = blockRewardRemaining;
        }

        public async Task PayoutAsync(Balance[] balances)
        {
            Contract.RequiresNonNull(balances, nameof(balances));

            // simple balances first
            var simpleBalances = balances
                .Where(x => !x.Address.Contains(PayoutConstants.PayoutInfoSeperator))
                .ToArray();

            if(simpleBalances.Length > 0)
                await PayoutBatch(simpleBalances);

            // balances with paymentIds
            var extraConfig = poolConfig.PaymentProcessing.Extra.SafeExtensionDataAs<MoneroPoolPaymentProcessingConfigExtra>();

            var minimumPaymentToPaymentId = extraConfig?.MinimumPaymentToPaymentId ?? poolConfig.PaymentProcessing.MinimumPayment;

            var paymentIdBalances = balances.Except(simpleBalances)
                .Where(x=> x.Amount >= minimumPaymentToPaymentId)
                .ToArray();

            foreach (var balance in paymentIdBalances)
                await PayoutToPaymentId(balance);
        }

        #endregion // IPayoutHandler
    }
}
