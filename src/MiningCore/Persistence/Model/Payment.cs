using System;
using MiningCore.Configuration;

namespace MiningCore.Persistence.Model
{
    public class Payment
    {
        public long Id { get; set; }
        public string PoolId { get; set; }
        public CoinType Coin { get; set; }
        public string Address { get; set; }
        public decimal Amount { get; set; }
        public string TransactionConfirmationData { get; set; }
        public DateTime Created { get; set; }
    }
}