using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Ergo.Configuration;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Payments;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;
using Miningcore.Util;
using Block = Miningcore.Persistence.Model.Block;
using Contract = Miningcore.Contracts.Contract;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Ergo
{
    [CoinFamily(CoinFamily.Ergo)]
    public class ErgoPayoutHandler : PayoutHandlerBase,
        IPayoutHandler
    {
        public ErgoPayoutHandler(
            IComponentContext ctx,
            IConnectionFactory cf,
            IMapper mapper,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo,
            IHttpClientFactory httpClientFactory,
            IMasterClock clock,
            IMessageBus messageBus) :
            base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, messageBus)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Contract.RequiresNonNull(paymentRepo, nameof(paymentRepo));
            Contract.RequiresNonNull(httpClientFactory, nameof(httpClientFactory));

            this.ctx = ctx;
            this.httpClientFactory = httpClientFactory;
        }

        protected readonly IComponentContext ctx;
        protected ErgoClient daemon;
        private ErgoPoolConfigExtra extraPoolConfig;
        private readonly IHttpClientFactory httpClientFactory;
        private string network;
        private ErgoPaymentProcessingConfigExtra extraPoolPaymentProcessingConfig;

        protected override string LogCategory => "Ergo Payout Handler";

        private class PaymentException : Exception
        {
            public PaymentException(string msg) : base(msg)
            {
            }
        }

        private void ReportAndRethrowApiError(string action, Exception ex, bool rethrow = true)
        {
            var error = ex.Message;

            if(ex is ApiException<ApiError> apiException)
                error = apiException.Result.Detail ?? apiException.Result.Reason;

            logger.Warn(() => $"{action}: {error}");

            if(rethrow)
                throw ex;
        }

        private async Task UnlockWallet()
        {
            logger.Info(() => $"[{LogCategory}] Unlocking wallet");

            var walletPassword = extraPoolPaymentProcessingConfig.WalletPassword ?? string.Empty;

            await Guard(() => daemon.WalletUnlockAsync(new Body4 {Pass = walletPassword}), ex =>
            {
                if (ex is ApiException<ApiError> apiException)
                {
                    var error = apiException.Result.Detail;

                    if (error != null && !error.ToLower().Contains("already unlocked"))
                        throw new PaymentException($"Failed to unlock wallet: {error}");
                }

                else
                    throw ex;
            });

            logger.Info(() => $"[{LogCategory}] Wallet unlocked");
        }

        private async Task LockWallet()
        {
            logger.Info(() => $"[{LogCategory}] Locking wallet");

            await Guard(() => daemon.WalletLockAsync(),
                ex => ReportAndRethrowApiError("Failed to lock wallet", ex));

            logger.Info(() => $"[{LogCategory}] Wallet locked");
        }

        #region IPayoutHandler

        public virtual async Task ConfigureAsync(ClusterConfig clusterConfig, PoolConfig poolConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

            logger = LogUtil.GetPoolScopedLogger(typeof(ErgoPayoutHandler), poolConfig);

            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;

            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<ErgoPoolConfigExtra>();
            extraPoolPaymentProcessingConfig = poolConfig.PaymentProcessing.Extra.SafeExtensionDataAs<ErgoPaymentProcessingConfigExtra>();

            daemon = ErgoClientFactory.CreateClient(poolConfig, clusterConfig, null);

            // detect chain
            var info = await daemon.GetNodeInfoAsync();
            network = ErgoConstants.RegexChain.Match(info.Name).Groups[1].Value.ToLower();
        }

        public virtual async Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(blocks, nameof(blocks));

            if(blocks.Length == 0)
                return blocks;

            var coin = poolConfig.Template.As<ErgoCoinTemplate>();
            var pageSize = 100;
            var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
            var result = new List<Block>();
            var minConfirmations = extraPoolConfig?.MinimumConfirmations ?? (network == "mainnet" ? 720 : 72);
            var minerRewardsPubKey = await daemon.MiningReadMinerRewardPubkeyAsync();
            var minerRewardsAddress = await daemon.MiningReadMinerRewardAddressAsync();

            for(var i = 0; i < pageCount; i++)
            {
                // get a page full of blocks
                var page = blocks
                    .Skip(i * pageSize)
                    .Take(pageSize)
                    .ToArray();

                // fetch header ids for blocks in page
                var headerBatch = page.Select(block => daemon.GetFullBlockAtAsync((int) block.BlockHeight)).ToArray();

                await Guard(()=> Task.WhenAll(headerBatch),
                    ex=> logger.Debug(ex));

                for(var j = 0; j < page.Length; j++)
                {
                    var block = page[j];
                    var headerTask = headerBatch[j];

                    if(!headerTask.IsCompletedSuccessfully)
                    {
                        if(headerTask.IsFaulted)
                            logger.Warn(()=> $"Failed to fetch block {block.BlockHeight}: {headerTask.Exception?.InnerException?.Message ?? headerTask.Exception?.Message}");
                        else
                            logger.Warn(()=> $"Failed to fetch block {block.BlockHeight}: {headerTask.Status.ToString().ToLower()}");

                        continue;
                    }

                    var headerIds = headerTask.Result;

                    // fetch blocks
                    var blockBatch = headerIds.Select(x=> daemon.GetFullBlockByIdAsync(x)).ToArray();

                    await Guard(()=> Task.WhenAll(blockBatch),
                        ex=> logger.Debug(ex));

                    var blockHandled = false;
                    var pkMismatchCount = 0;
                    var nonceMismatchCount = 0;
                    var coinbaseNonWalletTxCount = 0;

                    foreach (var blockTask in blockBatch)
                    {
                        if(blockHandled)
                            break;

                        if(!blockTask.IsCompletedSuccessfully)
                            continue;

                        var fullBlock = blockTask.Result;

                        // only consider blocks with pow-solution pk matching ours
                        if(fullBlock.Header.PowSolutions.Pk != minerRewardsPubKey.RewardPubKey)
                        {
                            pkMismatchCount++;
                            continue;
                        }

                        // only consider blocks with pow-solution nonce matching what we have on file
                        if(fullBlock.Header.PowSolutions.N != block.TransactionConfirmationData)
                        {
                            nonceMismatchCount++;
                            continue;
                        }

                        var coinbaseWalletTxFound = false;

                        foreach(var blockTx in fullBlock.BlockTransactions.Transactions)
                        {
                            var walletTx = await Guard(()=> daemon.WalletGetTransactionAsync(blockTx.Id));
                            var coinbaseOutput = walletTx?.Outputs?.FirstOrDefault(x => x.Address == minerRewardsAddress.RewardAddress);

                            if(coinbaseOutput != null)
                            {
                                coinbaseWalletTxFound = true;

                                // enough confirmations?
                                if(walletTx.NumConfirmations >= minConfirmations)
                                {
                                    // matured and spendable coinbase transaction
                                    block.Status = BlockStatus.Confirmed;
                                    block.ConfirmationProgress = 1;
                                    block.Reward = (decimal) (coinbaseOutput.Value / ErgoConstants.SmallestUnit);
                                    result.Add(block);

                                    logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");

                                    messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);

                                    blockHandled = true;
                                    break;
                                }

                                else
                                {
                                    // update progress
                                    block.ConfirmationProgress = Math.Min(1.0d, (double) walletTx.NumConfirmations / minConfirmations);
                                    block.Reward = (decimal) (coinbaseOutput.Value / ErgoConstants.SmallestUnit);
                                    result.Add(block);

                                    messageBus.NotifyBlockConfirmationProgress(poolConfig.Id, block, coin);
                                }
                            }
                        }

                        if(!blockHandled && !coinbaseWalletTxFound)
                            coinbaseNonWalletTxCount++;
                    }

                    if(!blockHandled)
                    {
                        string orphanReason = null;

                        if(pkMismatchCount == blockBatch.Length)
                            orphanReason = "pk mismatch";
                        else if(nonceMismatchCount == blockBatch.Length)
                            orphanReason = "nonce mismatch";
                        else if(coinbaseNonWalletTxCount == blockBatch.Length)
                            orphanReason = "no related coinbase tx found in wallet";

                        if(!string.IsNullOrEmpty(orphanReason))
                        {
                            block.Status = BlockStatus.Orphaned;
                            block.Reward = 0;
                            result.Add(block);

                            logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned due to {orphanReason}");

                            messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                        }
                    }
                }
            }

            return result.ToArray();
        }

        public virtual Task CalculateBlockEffortAsync(IMiningPool pool, Block block, double accumulatedBlockShareDiff)
        {
            block.Effort = accumulatedBlockShareDiff * ErgoConstants.ShareMultiplier / block.NetworkDifficulty;

            return Task.FromResult(true);
        }

        public override double AdjustShareDifficulty(double difficulty)
        {
            return difficulty * ErgoConstants.ShareMultiplier;
        }

        public virtual async Task PayoutAsync(IMiningPool pool, Balance[] balances)
        {
            Contract.RequiresNonNull(balances, nameof(balances));

            // build args
            var amounts = balances
                .Where(x => x.Amount > 0)
                .ToDictionary(x => x.Address, x => Math.Round(x.Amount, 4));

            if(amounts.Count == 0)
                return;

            try
            {
                // get wallet status
                var status = await daemon.GetWalletStatusAsync();

                if(!status.IsInitialized)
                    throw new PaymentException($"Wallet is not initialized");

                if(!status.IsUnlocked)
                    await UnlockWallet();

                // get balance
                var walletBalances = await daemon.WalletBalancesAsync();
                logger.Info(() => $"[{LogCategory}] Current wallet balance is {FormatAmount(walletBalances.Balance / ErgoConstants.SmallestUnit)}");

                // Create request batch
                var requests = amounts.Select(x => new PaymentRequest
                {
                    Address = x.Key,
                    Value = (long) (x.Value * ErgoConstants.SmallestUnit),
                }).ToArray();

                logger.Info(() => $"[{LogCategory}] Paying out {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

                var txId = await Guard(()=> daemon.WalletPaymentTransactionGenerateAndSendAsync(requests), ex =>
                {
                    if(ex is ApiException<ApiError> apiException)
                    {
                        var error = apiException.Result.Detail ?? apiException.Result.Reason;

                        if(error.Contains("reason:"))
                            error = error.Substring(error.IndexOf("reason:"));

                        throw new PaymentException($"Payment transaction failed: {error}");
                    }

                    else
                        throw ex;
                });

                if(string.IsNullOrEmpty(txId))
                    throw new PaymentException("Payment transaction failed to return a transaction id");

                // payment successful
                logger.Info(() => $"[{LogCategory}] Payout transaction id: {txId}");

                await PersistPaymentsAsync(balances, txId);

                NotifyPayoutSuccess(poolConfig.Id, balances, new[] {txId}, null);
            }

            catch(PaymentException ex)
            {
                logger.Error(() => $"[{LogCategory}] {ex.Message}");

                NotifyPayoutFailure(poolConfig.Id, balances, ex.Message, null);
            }

            finally
            {
                await LockWallet();
            }
        }

        #endregion // IPayoutHandler
    }
}
