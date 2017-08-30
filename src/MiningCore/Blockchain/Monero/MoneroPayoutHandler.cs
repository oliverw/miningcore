using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using MiningCore.Blockchain.Monero.Configuration;
using MiningCore.Blockchain.Monero.DaemonRequests;
using MiningCore.Blockchain.Monero.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.DaemonInterface;
using MiningCore.Payments;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using MiningCore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Contract = MiningCore.Contracts.Contract;
using MC = MiningCore.Blockchain.Monero.MoneroCommands;
using MWC = MiningCore.Blockchain.Monero.MoneroWalletCommands;

namespace MiningCore.Blockchain.Monero
{
    [CoinMetadata(CoinType.XMR)]
    public class MoneroPayoutHandler : PayoutHandlerBase,
        IPayoutHandler
    {
        public MoneroPayoutHandler(IConnectionFactory cf, IMapper mapper,
            DaemonClient daemon,
            DaemonClient walletDaemon,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo) :
            base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo)
        {
            Contract.RequiresNonNull(daemon, nameof(daemon));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Contract.RequiresNonNull(paymentRepo, nameof(paymentRepo));

            this.daemon = daemon;
            this.walletDaemon = walletDaemon;
        }

        private readonly DaemonClient daemon;
        private readonly DaemonClient walletDaemon;
        private MoneroNetworkType? networkType;

        protected override string LogCategory => "Monero Payout Handler";

        private void HandleTransferResponse(DaemonResponse<TransferResponse> response, params Balance[] balances)
        {
            if (response.Error == null)
            {
                var txHash = response.Response.TxHash;

                // check result
                if (string.IsNullOrEmpty(txHash))
                    logger.Error(() => $"[{LogCategory}] Daemon command '{MWC.Transfer}' did not return a transaction id!");
                else
                    logger.Info(() => $"[{LogCategory}] Payout transaction id: {txHash}, TxFee was {FormatAmount((decimal) response.Response.Fee / MoneroConstants.Piconero)}");

                PersistPayments(balances, txHash);
            }

            else
            {
                logger.Error(() => $"[{LogCategory}] Daemon command '{MWC.Transfer}' returned error: {response.Error.Message} code {response.Error.Code}");
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

            HandleTransferResponse(result, balances);
        }

        private async Task PayoutToPaymentId(Balance balance)
        {
            // extract paymentId
            var address = (string)null;
            var paymentId = (string)null;

            var index = balance.Address.IndexOf(PaymentConstants.PayoutInfoSeperator);
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

            HandleTransferResponse(result, balance);
        }

        #region IPayoutHandler

        public void Configure(ClusterConfig clusterConfig, PoolConfig poolConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;

            logger = LogUtil.GetPoolScopedLogger(typeof(MoneroPayoutHandler), poolConfig);

            // configure standard daemon
            var daemonEndpoints = poolConfig.Daemons
                .Where(x => string.IsNullOrEmpty(x.Category))
                .ToArray();

            daemon.Configure(daemonEndpoints, MoneroConstants.DaemonRpcLocation);

            // configure wallet daemon
            var walletDaemonEndpoints = poolConfig.Daemons
                .Where(x => x.Category?.ToLower() == MoneroConstants.WalletDaemonCategory)
                .ToArray();

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
                .Where(x => !x.Address.Contains(PaymentConstants.PayoutInfoSeperator))
                .ToArray();

            if(simpleBalances.Length > 0)
                await PayoutBatch(simpleBalances);

            // balances with paymentIds
            var extraConfig = poolConfig.PaymentProcessing.Extra != null ?
                JToken.FromObject(poolConfig.PaymentProcessing.Extra).ToObject<MoneroPoolPaymentProcessingConfigExtra>()
                : null;

            var minimumPaymentToPaymentId = extraConfig?.MinimumPaymentToPaymentId ?? poolConfig.PaymentProcessing.MinimumPayment;

            var paymentIdBalances = balances.Except(simpleBalances)
                .Where(x=> x.Amount >= minimumPaymentToPaymentId)
                .ToArray();

            foreach (var balance in paymentIdBalances)
                await PayoutToPaymentId(balance);
        }

        public string FormatAmount(decimal amount)
        {
            return $"{amount:0.#####} {poolConfig.Coin.Type}";
        }

        #endregion // IPayoutHandler
    }
}
