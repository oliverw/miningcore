namespace Miningcore.Persistence.Model.Projections;

public record WorkerPerformanceStats
{
    public double Hashrate { get; init; }
    public double SharesPerSecond { get; init; }
}

public record WorkerPerformanceStatsContainer
{
    public DateTime Created { get; init; }
    public Dictionary<string, WorkerPerformanceStats> Workers { get; init; }
}

public class MinerStats
{
    public double PendingShares { get; init; }
    public decimal PendingBalance { get; init; }
    public decimal TotalPaid { get; init; }
    public decimal TodayPaid { get; init; }
    public Payment LastPayment { get; set; }
    public WorkerPerformanceStatsContainer Performance { get; set; }
    public MinerWorkerPerformanceStats[] PerformanceStats { get; init; }
}
