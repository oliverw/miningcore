using System;

namespace Miningcore.Persistence.Model
{
    public class BalanceChange
    {
        public long Id { get; set; }
        public string PoolId { get; set; }
        public string Address { get; set; }

        /// <summary>
        /// Amount owed in pool-base-currency (ie. Bitcoin, not Satoshis)
        /// </summary>
        public decimal Amount { get; set; }

        public string Usage { get; set; }

        public DateTime Created { get; set; }
    }
}
