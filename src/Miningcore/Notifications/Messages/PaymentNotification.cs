using System;
using System.Collections.Generic;
using System.Text;

namespace Miningcore.Notifications.Messages
{
    public class PaymentNotification
    {
        public PaymentNotification(string poolId, string poolCurrencySymbol, string error, decimal amount, int recpientsCount, string[] txIds, string[] txLinks, decimal? txFee)
        {
            PoolId = poolId;
            Error = error;
            Amount = amount;
            RecpientsCount = recpientsCount;
            TxIds = txLinks;
            TxLinks = txLinks;
            TxFee = txFee;
        }

        public PaymentNotification(string poolId, string poolCurrencySymbol, string error, decimal amount) : this(poolId, poolCurrencySymbol, error, amount, 0, null, null, null)
        {
        }

        public string PoolId { get; }
        public string PoolCurrencySymbol { get; }
        public decimal? TxFee { get; }
        public string[] TxIds { get; }
        public string[] TxLinks { get; }
        public int RecpientsCount { get; }
        public decimal Amount { get; }
        public string Error { get; }
    }
}
