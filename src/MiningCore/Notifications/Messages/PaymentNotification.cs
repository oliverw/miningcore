using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Notifications.Messages
{
    public class PaymentNotification
    {
        public PaymentNotification(string poolId, string error, decimal amount, int recpientsCount, string txInfo, decimal? txFee)
        {
            PoolId = poolId;
            Error = error;
            Amount = amount;
            RecpientsCount = recpientsCount;
            TxInfo = txInfo;
            TxFee = txFee;
        }

        public PaymentNotification(string poolId, string error, decimal amount) : this(poolId, error, amount, 0, null, null)
        {
        }

        public decimal? TxFee { get; }
        public string TxInfo { get; }
        public int RecpientsCount { get; }
        public decimal Amount { get; }
        public string Error { get; }
        public string PoolId { get; }
    }
}
