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

        private ZCashPoolConfigExtra poolExtraConfig;
        protected override string LogCategory => "ZCash Payout Handler";
        protected const decimal TransferFee = 0.0001m;

        #region IPayoutHandler

        public override async Task ConfigureAsync(ClusterConfig clusterConfig, PoolConfig poolConfig)
        {
            await base.ConfigureAsync(clusterConfig, poolConfig);

            poolExtraConfig = poolConfig.Extra.SafeExtensionDataAs<ZCashPoolConfigExtra>();
        }

        public override async Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, Block block, PoolConfig pool)
        {
            await ShieldCoinbaseAsync();

            return await base.UpdateBlockRewardBalancesAsync(con, tx, block, pool);
        }

        public override async Task PayoutAsync(Balance[] balances)
        {
            Contract.RequiresNonNull(balances, nameof(balances));

            // send in batches with no more than 50 recipients to avoid running into tx size limits
            var pageSize = 50;
            var pageCount = (int)Math.Ceiling(balances.Length / (double)pageSize);

            for (var i = 0; i < pageCount; i++)
            {
                // get a page full of balances
                var page = balances
                    .Skip(i * pageSize)
                    .Take(pageSize)
                    .ToArray();

                // build args
                var amounts = page
                    .Where(x => x.Amount > 0)
                    .Select(x => new ZSendManyRecipient { Address = x.Address, Amount = Math.Round(x.Amount, 8) })
                    .ToList();

                if (amounts.Count == 0)
                    return;

                logger.Info(() => $"[{LogCategory}] Paying out {FormatAmount(page.Sum(x => x.Amount))} to {page.Length} addresses");

                var args = new object[]
                {
                    poolExtraConfig.ZAddress,   // default account
                    amounts,                    // addresses and associated amounts
                    10,                         // only spend funds covered by this many confirmations
                    TransferFee
                };

                // send command
                var result = await daemon.ExecuteCmdSingleAsync<string>(ZCashCommands.ZSendMany, args);

                if (result.Error == null)
                {
                    var operationId = result.Response;

                    // check result
                    if (string.IsNullOrEmpty(operationId))
                        logger.Error(() => $"[{LogCategory}] {ZCashCommands.ZSendMany} did not return a operation id!");
                    else
                    {
                        logger.Info(() => $"[{LogCategory}] Tracking payout operation id: {operationId}");

                        var continueWaiting = true;

                        while(continueWaiting)
                        {
                            var operationResultResponse = await daemon.ExecuteCmdSingleAsync<ZCashAsyncOperationStatus[]>(
                                ZCashCommands.ZGetOperationResult, new object[] { new object[] { operationId }});

                            if (operationResultResponse.Error == null &&
                                operationResultResponse.Response?.Any(x => x.OperationId == operationId) == true)
                            {
                                var operationResult = operationResultResponse.Response.First(x => x.OperationId == operationId);

                                if (!Enum.TryParse(operationResult.Status, true, out ZOperationStatus status))
                                {
                                    logger.Error(() => $"Unrecognized operation status: {operationResult.Status}");
                                    break;
                                }

                                switch (status)
                                {
                                    case ZOperationStatus.Success:
                                        var txId = operationResult.Result?.Value<string>("txid") ?? string.Empty;
                                        logger.Info(() => $"[{LogCategory}]      completed with transaction id: {txId}");

                                        PersistPayments(page, txId);
                                        NotifyPayoutSuccess(poolConfig.Id, page, new[] {txId}, null);

                                        continueWaiting = false;
                                        continue;

                                    case ZOperationStatus.Cancelled:
                                    case ZOperationStatus.Failed:
                                        logger.Error(() => $"{ZCashCommands.ZSendMany} failed: {operationResult.Error.Message} code {operationResult.Error.Code}");
                                        NotifyPayoutFailure(poolConfig.Id, page, $"{ZCashCommands.ZSendMany} failed: {operationResult.Error.Message} code {operationResult.Error.Code}", null);

                                        continueWaiting = false;
                                        continue;
                                }
                            }

                            logger.Info(() => $"[{LogCategory}] Waiting for completion: {operationId}");
                            await Task.Delay(TimeSpan.FromSeconds(10));
                        }
                    }
                }

                else
                {
                    logger.Error(() => $"[{LogCategory}] {ZCashCommands.ZSendMany} returned error: {result.Error.Message} code {result.Error.Code}");

                    NotifyPayoutFailure(poolConfig.Id, page, $"{ZCashCommands.ZSendMany} returned error: {result.Error.Message} code {result.Error.Code}", null);
                }
            }
        }

        #endregion // IPayoutHandler

        /// <summary>
        /// ZCash coins are mined into a t-addr (transparent address), but can only be
        /// spent to a z-addr (shielded address), and must be swept out of the t-addr
        /// in one transaction with no change.
        /// </summary>
        private async Task ShieldCoinbaseAsync()
        {
            logger.Info(() => $"[{LogCategory}] Shielding ZCash Coinbase funds");

            var args = new object[]
            {
                poolConfig.Address,         // source: pool's t-addr receiving coinbase rewards
                poolExtraConfig.ZAddress,   // dest:   pool's z-addr
            };

            var result = await daemon.ExecuteCmdSingleAsync<string>(ZCashCommands.ZShieldCoinbase, args);

            if (result.Error != null)
            {
                logger.Error(() => $"[{LogCategory}] {ZCashCommands.ZShieldCoinbase} returned error: {result.Error.Message} code {result.Error.Code}");
                return;
            }

            var operationId = result.Response;

            logger.Info(() => $"[{LogCategory}] {ZCashCommands.ZShieldCoinbase} operation id: {operationId}");

            var continueWaiting = true;

            while (continueWaiting)
            {
                var operationResultResponse = await daemon.ExecuteCmdSingleAsync<ZCashAsyncOperationStatus[]>(
                    ZCashCommands.ZGetOperationResult, new object[] { new object[] { operationId } });

                if (operationResultResponse.Error == null &&
                    operationResultResponse.Response?.Any(x => x.OperationId == operationId) == true)
                {
                    var operationResult = operationResultResponse.Response.First(x => x.OperationId == operationId);

                    if (!Enum.TryParse(operationResult.Status, true, out ZOperationStatus status))
                    {
                        logger.Error(() => $"Unrecognized operation status: {operationResult.Status}");
                        break;
                    }

                    switch (status)
                    {
                        case ZOperationStatus.Success:
                            logger.Info(() => $"[{LogCategory}] {ZCashCommands.ZShieldCoinbase} successful");

                            continueWaiting = false;
                            continue;

                        case ZOperationStatus.Cancelled:
                        case ZOperationStatus.Failed:
                            logger.Error(() => $"{ZCashCommands.ZShieldCoinbase} failed: {operationResult.Error.Message} code {operationResult.Error.Code}");

                            continueWaiting = false;
                            continue;
                    }
                }

                logger.Info(() => $"[{LogCategory}] Waiting for operation completion: {operationId}");
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }
    }
}
