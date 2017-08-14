using System;
using System.Data.Common;
using System.Linq;
using AutoMapper;
using CodeContracts;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using Newtonsoft.Json;
using NLog;
using Polly;

namespace MiningCore.Payments
{
    public abstract class PayoutHandlerBase
    {
        private const int RetryCount = 8;
        protected readonly IBalanceRepository balanceRepo;
        protected readonly IBlockRepository blockRepo;
        protected readonly IConnectionFactory cf;
        protected readonly IMapper mapper;
        protected readonly IPaymentRepository paymentRepo;
        protected readonly IShareRepository shareRepo;
        protected ClusterConfig clusterConfig;
        private Policy faultPolicy;

        protected ILogger logger;
        protected PoolConfig poolConfig;

        protected PayoutHandlerBase(IConnectionFactory cf, IMapper mapper,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo)
        {
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Contract.RequiresNonNull(paymentRepo, nameof(paymentRepo));

            this.cf = cf;
            this.mapper = mapper;
            this.shareRepo = shareRepo;
            this.blockRepo = blockRepo;
            this.balanceRepo = balanceRepo;
            this.paymentRepo = paymentRepo;

            BuildFaultHandlingPolicy();
        }

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
                        foreach (var balance in balances)
                        {
                            // record payment
                            var payment = new Payment
                            {
                                PoolId = poolConfig.Id,
                                Coin = poolConfig.Coin.Type,
                                Address = balance.Address,
                                Amount = balance.Amount,
                                Created = DateTime.UtcNow,
                                TransactionConfirmationData = transactionConfirmation
                            };

                            paymentRepo.Insert(con, tx, payment);

                            // subtract balance
                            balanceRepo.AddAmount(con, tx, poolConfig.Id, poolConfig.Coin.Type, balance.Address,
                                -balance.Amount);
                        }
                    });
                });
            }

            catch (Exception ex)
            {
                logger.Error(ex, () => $"[{LogCategory}] Failed to persist the following payments: " +
                                       $"{JsonConvert.SerializeObject(balances.Where(x => x.Amount > 0).ToDictionary(x => x.Address, x => x.Amount))}");
                throw;
            }
        }
    }
}
