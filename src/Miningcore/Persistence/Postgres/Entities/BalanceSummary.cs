namespace Miningcore.Persistence.Postgres.Entities
{
    public class BalanceSummary
    {
        public int NoOfDaysOld { get; set; }
        public int CustomersCount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal TotalAmountOverThreshold { get; set; }
    }
}
