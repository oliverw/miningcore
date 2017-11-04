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
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.ZCash.Configuration;
using MiningCore.Blockchain.ZCash.DaemonRequests;
using MiningCore.Blockchain.ZCash.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using MiningCore.Time;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Blockchain.ZCash
{
    [CoinMetadata(CoinType.ZEC)]
    public class ZCashPayoutHandler : BitcoinPayoutHandler
    {
        public ZCashPayoutHandler(
            IComponentContext ctx,
            IConnectionFactory cf,
            IMapper mapper,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo,
            IMasterClock clock,
            NotificationService notificationService) :
            base(ctx, cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, notificationService)
        {
        }

        private ZCashPoolConfigExtra extraConfig;
        protected override string LogCategory => "ZCash Payout Handler";
        protected const decimal TransferFee = 0.0001m;

        #region IPayoutHandler

        public override void Configure(ClusterConfig clusterConfig, PoolConfig poolConfig)
        {
            base.Configure(clusterConfig, poolConfig);

            extraConfig = poolConfig.Extra.SafeExtensionDataAs<ZCashPoolConfigExtra>();
        }

        public override async Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, Block block, PoolConfig pool)
        {
            var result = await base.UpdateBlockRewardBalancesAsync(con, tx, block, pool);

            // Transfer entire block reward to z-addr
            await TransferTransparentPoolBalance();

            return result;
        }

        public override async Task PayoutAsync(Balance[] balances)
        {
            Contract.RequiresNonNull(balances, nameof(balances));

            // build args
            var amounts = balances
                .Where(x => x.Amount > 0)
                .Select(x => new ZSendManyRecipient { Address = x.Address, Amount = Math.Round(x.Amount, 8) })
                .ToList();

            if (amounts.Count == 0)
                return;

            logger.Info(() => $"[{LogCategory}] Paying out {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

            var args = new object[]
            {
                extraConfig.ZAddress,   // default account
                amounts,                // addresses and associated amounts
                10,                     // only spend funds covered by this many confirmations
                TransferFee
            };

            // send command
            var result = await daemon.ExecuteCmdSingleAsync<string>(ZCashCommands.ZSendMany, args);

            if (result.Error == null)
            {
                var operationId = result.Response;

                // check result
                if (string.IsNullOrEmpty(operationId))
                    logger.Error(() => $"[{LogCategory}] Daemon command '{ZCashCommands.ZSendMany}' did not return a operation id!");
                else
                {
                    logger.Info(() => $"[{LogCategory}] Tracking payout operation id: {operationId}");

                    while (true)
                    {
                        var operationResultResponse = await daemon.ExecuteCmdSingleAsync<ZCashAsyncOperationStatus[]>(
                            ZCashCommands.ZGetOperationResult);

                        if (operationResultResponse.Error == null && 
                            operationResultResponse.Response?.Any(x=> x.OperationId == operationId) == true)
                        {
                            var operationResult = operationResultResponse.Response.First(x => x.OperationId == operationId);

                            switch (operationResult.Status.ToLower())
                            {
                                case "success":
                                    // extract transaction id
                                    var txId = operationResult.Result?.Value<string>("txid") ?? string.Empty;
                                    logger.Info(() => $"[{LogCategory}] Payout transaction id: {txId}");

                                    PersistPayments(balances, txId);
                                    NotifyPayoutSuccess(balances, new[] { txId }, null);
                                    break;

                                case "cancelled":
                                case "failed":
                                    NotifyPayoutFailure(balances, $"ZCash Payout operation failed: {operationResult.Error.Message} code {operationResult.Error.Code}", null);
                                    break;
                            }
                        }

                        logger.Info(() => $"[{LogCategory}] Waiting for ZCash Payout to complete: {operationId}");
                        await Task.Delay(10);
                    }
                }
            }

            else
            {
                logger.Error(() => $"[{LogCategory}] Daemon command '{BitcoinCommands.SendMany}' returned error: {result.Error.Message} code {result.Error.Code}");

                NotifyPayoutFailure(balances, $"Daemon command '{BitcoinCommands.SendMany}' returned error: {result.Error.Message} code {result.Error.Code}", null);
            }
        }

        #endregion // IPayoutHandler

        /// <summary>
        /// ZCash coins are mined into a t-addr (transparent address), but can only be 
        /// spent to a z -addr (shielded address), and must be swept out of the t-addr 
        /// in one transaction with no change.
        /// </summary>
        private async Task TransferTransparentPoolBalance()
        {
            // get t-addr balance
            var balanceResult = await daemon.ExecuteCmdSingleAsync<object>(BitcoinCommands.GetBalance);

            if (balanceResult.Error != null)
            {
                logger.Error(() => $"[{LogCategory}] Daemon command '{BitcoinCommands.GetBalance}' returned error: {balanceResult.Error.Message} code {balanceResult.Error.Code}");
                return;
            }

            var balance = (decimal) (double) balanceResult.Response;

            if (balance > 0)
            {
                logger.Info(() => $"[{LogCategory}] Transferring {FormatAmount(balance)} to pool's z-addr");

                // transfer to z-addr
                var recipient = new ZSendManyRecipient
                {
                    Address = extraConfig.ZAddress,
                    Amount = balance - TransferFee
                };

                var args = new object[]
                {
                    poolConfig.Address, // default account
                    new object[]		// addresses and associated amounts
                    {
                        recipient
                    },
                    10,					// only spend funds covered by this many confirmations
                    TransferFee
                };

                // send command
                var sendResult = await daemon.ExecuteCmdSingleAsync<string>(ZCashCommands.ZSendMany, args);

                if (sendResult.Error != null)
                {
                    logger.Error(() => $"[{LogCategory}] Daemon command '{ZCashCommands.ZSendMany}' returned error: {balanceResult.Error.Message} code {balanceResult.Error.Code}");
                    return;
                }

                var operationId = sendResult.Response;

                logger.Info(() => $"[{LogCategory}] ZCash Balance Transfer operation id: {operationId}");

                while (true)
                {
                    var operationResultResponse = await daemon.ExecuteCmdSingleAsync<ZCashAsyncOperationStatus[]>(
                        ZCashCommands.ZGetOperationResult);

                    if (operationResultResponse.Error == null &&
                        operationResultResponse.Response?.Any(x => x.OperationId == operationId) == true)
                    {
                        var operationResult = operationResultResponse.Response.First(x => x.OperationId == operationId);

                        switch (operationResult.Status.ToLower())
                        {
                            case "success":
                                // extract transaction id
                                var txId = operationResult.Result?.Value<string>("txid") ?? string.Empty;
                                logger.Info(() => $"[{LogCategory}] ZCash Balance Transfer transaction id: {txId}");
                                break;

                            case "cancelled":
                            case "failed":
                                break;
                        }
                    }

                    logger.Info(() => $"[{LogCategory}] Waiting for ZCash Balance transfer to complete: {operationId}");
                    await Task.Delay(10);
                }
            }
        }
    }
}
