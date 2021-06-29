using System;

namespace Miningcore.Persistence.Model
{
    public class MinerWorkerStatsPreAgg
    {
        public string PoolId { get; set; }
        public string Miner { get; set; }
        public string Worker { get; set; }

        public long ShareCount { get; set; }
        public double SharesAccumulated { get; set; }

        public DateTime Created { get; set; }
        public DateTime Updated { get; set; }
    }
}
