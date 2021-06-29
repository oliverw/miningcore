using System;
using System.Collections.Generic;

namespace Miningcore.Persistence.Model.Projections
{
    public class WorkerPerformanceStats
    {
        public double Hashrate { get; set; }
        public double SharesPerSecond { get; set; }
    }

    public class WorkerPerformanceStatsContainer
    {
        public DateTime Created { get; set; }
        public Dictionary<string, WorkerPerformanceStats> Workers { get; set; }
    }

    public class MinerStats
    {
        public ulong PendingShares { get; set; }
        public decimal PendingBalance { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal TodayPaid { get; set; }
        public Payment LastPayment { get; set; }
        public WorkerPerformanceStatsContainer Performance { get; set; }
        public MinerWorkerPerformanceStats[] PerformanceStats { get; set; }
    }
}
