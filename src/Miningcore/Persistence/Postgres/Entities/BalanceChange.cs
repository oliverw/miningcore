using System;

namespace Miningcore.Persistence.Postgres.Entities
{
    public class BalanceChange
    {
        public long Id { get; set; }
        public string PoolId { get; set; }
        public string Address { get; set; }
        public decimal Amount { get; set; }
        public string Usage { get; set; }
        public DateTime Created { get; set; }
    }
}
