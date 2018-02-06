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
using System.Data.Common;
using System.Linq;
using AutoMapper;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using MiningCore.Time;
using Newtonsoft.Json;
using NLog;
using Polly;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Payments
{
    public abstract class PayoutHandlerBase
    {
        protected PayoutHandlerBase(IConnectionFactory cf, IMapper mapper,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo,
            IMasterClock clock,
            NotificationService notificationService)
        {
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Contract.RequiresNonNull(paymentRepo, nameof(paymentRepo));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(notificationService, nameof(notificationService));

            this.cf = cf;
            this.mapper = mapper;
            this.clock = clock;
            this.shareRepo = shareRepo;
            this.blockRepo = blockRepo;
            this.balanceRepo = balanceRepo;
            this.paymentRepo = paymentRepo;
            this.notificationService = notificationService;

            BuildFaultHandlingPolicy();
        }

        protected readonly IBalanceRepository balanceRepo;
        protected readonly IBlockRepository blockRepo;
        protected readonly IConnectionFactory cf;
        protected readonly IMapper mapper;
        protected readonly IPaymentRepository paymentRepo;
        protected readonly IShareRepository shareRepo;
        protected readonly IMasterClock clock;
        protected readonly NotificationService notificationService;
        protected ClusterConfig clusterConfig;
        private Policy faultPolicy;

        protected ILogger logger;
        protected PoolConfig poolConfig;
        private const int RetryCount = 8;

        protected abstract string LogCategory { get; }

        protected void BuildFaultHandlingPolicy()
        {
            var retry = Policy
                .Handle<DbException>()
                .Or<TimeoutException>()
                .WaitAndRetry(RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), OnRetry);

            faultPolicy = retry;
        }

        protected virtual void OnRetry(Exception ex, TimeSpan timeSpan, int retry, object context)
        {
            logger.Warn(() => $"[{LogCategory}] Retry {1} in {timeSpan} due to: {ex}");
        }

        protected virtual void PersistPayments(Balance[] balances, string transactionConfirmation)
        {
            try
            {
                faultPolicy.Execute(() =>
                {
                    cf.RunTx((con, tx) =>
                    {
                        foreach(var balance in balances)
                        {
                            if (!string.IsNullOrEmpty(transactionConfirmation) &&
                                !poolConfig.RewardRecipients.Any(x=> x.Address == balance.Address))
                            {
                                // record payment
                                var payment = new Payment
                                {
                                    PoolId = poolConfig.Id,
                                    Coin = poolConfig.Coin.Type,
                                    Address = balance.Address,
                                    Amount = balance.Amount,
                                    Created = clock.Now,
                                    TransactionConfirmationData = transactionConfirmation
                                };

                                paymentRepo.Insert(con, tx, payment);
                            }

                            // reset balance
                            logger.Debug(() => $"[{LogCategory}] Resetting balance of {balance.Address}");
                            balanceRepo.AddAmount(con, tx, poolConfig.Id, poolConfig.Coin.Type, balance.Address, -balance.Amount);
                        }
                    });
                });
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"[{LogCategory}] Failed to persist the following payments: " +
                    $"{JsonConvert.SerializeObject(balances.Where(x => x.Amount > 0).ToDictionary(x => x.Address, x => x.Amount))}");
                throw;
            }
        }

        public string FormatAmount(decimal amount)
        {
            return $"{amount:0.#####} {poolConfig.Coin.Type}";
        }

        protected virtual void NotifyPayoutSuccess(string poolId, Balance[] balances, string[] txHashes, decimal? txFee)
        {
            // admin notifications
            if (clusterConfig.Notifications?.Admin?.Enabled == true &&
                clusterConfig.Notifications?.Admin?.NotifyPaymentSuccess == true)
            {
                // prepare tx link
                var txInfo = string.Join(", ", txHashes);

                if (CoinMetaData.TxInfoLinks.TryGetValue(poolConfig.Coin.Type, out var baseUrl))
                    txInfo = string.Join(", ", txHashes.Select(txHash => $"<a href=\"{string.Format(baseUrl, txHash)}\">{txHash}</a>"));

                notificationService.NotifyPaymentSuccess(poolId, balances.Sum(x => x.Amount), balances.Length, txInfo, txFee);
            }
        }

        protected virtual void NotifyPayoutFailure(string poolId, Balance[] balances, string error, Exception ex)
        {
            notificationService.NotifyPaymentFailure(poolId, balances.Sum(x => x.Amount), error ?? ex?.Message);
        }
    }
}
