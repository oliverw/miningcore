using System;
using System.Collections.Generic;
using System.Text;

namespace Miningcore.Notifications.Messages
{
    public record PaymentNotification
    {
        public PaymentNotification(string poolId, string error, decimal amount, string symbol, int recpientsCount, string[] txIds, string[] txExplorerLinks, decimal? txFee)
        {
            PoolId = poolId;
            Error = error;
            Amount = amount;
            RecpientsCount = recpientsCount;
            TxIds = txIds;
            TxFee = txFee;
            Symbol = symbol;
            TxExplorerLinks = txExplorerLinks;
        }

        public PaymentNotification(string poolId, string error, decimal amount, string symbol) : this(poolId, error, amount, symbol, 0, null, null, null)
        {
        }

        public PaymentNotification()
        {
        }

        public string PoolId { get; set; }
        public decimal? TxFee { get; set; }
        public string[] TxIds { get; set; }
        public string[] TxExplorerLinks { get; set; }
        public string Symbol { get; set; }
        public int RecpientsCount { get; set; }
        public decimal Amount { get; set; }
        public string Error { get; set; }
    }
}
