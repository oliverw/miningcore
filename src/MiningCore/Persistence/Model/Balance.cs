using System;
using MiningCore.Configuration;

namespace MiningCore.Persistence.Model
{
    public class Balance
    {
        public string PoolId { get; set; }
        public CoinType Coin { get; set; }
        public string Address { get; set; }

        /// <summary>
        /// Amount owed in pool-base-currency (ie. Bitcoin, not Satoshis)
        /// </summary>
        public decimal Amount { get; set; }

        public DateTime Created { get; set; }
        public DateTime Updated { get; set; }
    }
}