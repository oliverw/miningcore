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
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Configuration;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using MiningCore.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Blockchain.Dash
{
    [CoinMetadata(CoinType.DASH, CoinType.GBX, CoinType.CRC)]
    public class DashPayoutHandler : BitcoinPayoutHandler
    {
        public DashPayoutHandler(
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

        protected override string LogCategory => "Dash Payout Handler";

        #region IPayoutHandler

        public override async Task PayoutAsync(Balance[] balances)
        {
            Contract.RequiresNonNull(balances, nameof(balances));

            // build args
            var amounts = balances
                .Where(x => x.Amount > 0)
                .ToDictionary(x => x.Address, x => Math.Round(x.Amount, 8));

            if (amounts.Count == 0)
                return;

            logger.Info(() => $"[{LogCategory}] Paying out {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

            object[] args;

            if (extraPoolPaymentProcessingConfig?.MinersPayTxFees == true)
            {
                var comment = (poolConfig.PoolName ?? clusterConfig.ClusterName ?? "MiningCore").Trim() + " Payment";
                var subtractFeesFrom = amounts.Keys.ToArray();

                args = new object[]
                {
                    string.Empty,           // default account
                    amounts,                // addresses and associated amounts
                    1,                      // only spend funds covered by this many confirmations
                    false,                  // Whether to add confirmations to transactions locked via InstantSend
                    comment,                // tx comment
                    subtractFeesFrom,       // distribute transaction fee equally over all recipients
                    false,                  // use_is: Send this transaction as InstantSend
                    false,                  // Use anonymized funds only
                };
            }

            else
            {
                args = new object[]
                {
                    string.Empty,           // default account
                    amounts,                // addresses and associated amounts
                };
            }

            var didUnlockWallet = false;

            // send command
            tryTransfer:
            var result = await daemon.ExecuteCmdSingleAsync<string>(BitcoinCommands.SendMany, args, new JsonSerializerSettings());

            if (result.Error == null)
            {
                if (didUnlockWallet)
                {
                    // lock wallet
                    logger.Info(() => $"[{LogCategory}] Locking wallet");
                    await daemon.ExecuteCmdSingleAsync<JToken>(BitcoinCommands.WalletLock);
                }

                // check result
                var txId = result.Response;

                if (string.IsNullOrEmpty(txId))
                    logger.Error(() => $"[{LogCategory}] {BitcoinCommands.SendMany} did not return a transaction id!");
                else
                    logger.Info(() => $"[{LogCategory}] Payout transaction id: {txId}");

                PersistPayments(balances, txId);

                NotifyPayoutSuccess(poolConfig.Id, balances, new[] { txId }, null);
            }

            else
            {
                if (result.Error.Code == (int)BitcoinRPCErrorCode.RPC_WALLET_UNLOCK_NEEDED && !didUnlockWallet)
                {
                    if (!string.IsNullOrEmpty(extraPoolPaymentProcessingConfig?.WalletPassword))
                    {
                        logger.Info(() => $"[{LogCategory}] Unlocking wallet");

                        var unlockResult = await daemon.ExecuteCmdSingleAsync<JToken>(BitcoinCommands.WalletPassphrase, new[]
                        {
                            (object) extraPoolPaymentProcessingConfig.WalletPassword,
                            (object) 5  // unlock for N seconds
                        });

                        if (unlockResult.Error == null)
                        {
                            didUnlockWallet = true;
                            goto tryTransfer;
                        }

                        else
                            logger.Error(() => $"[{LogCategory}] {BitcoinCommands.WalletPassphrase} returned error: {result.Error.Message} code {result.Error.Code}");
                    }

                    else
                        logger.Error(() => $"[{LogCategory}] Wallet is locked but walletPassword was not configured. Unable to send funds.");
                }

                else
                {
                    logger.Error(() => $"[{LogCategory}] {BitcoinCommands.SendMany} returned error: {result.Error.Message} code {result.Error.Code}");

                    NotifyPayoutFailure(poolConfig.Id, balances, $"{BitcoinCommands.SendMany} returned error: {result.Error.Message} code {result.Error.Code}", null);
                }
            }
        }

        #endregion // IPayoutHandler
    }
}
