namespace Miningcore.Persistence.Model;

public record PoolStats
{
    public long Id { get; init; }
    public string PoolId { get; init; }

    public int ConnectedMiners { get; init; }
    public float PoolHashrate { get; init; }
    public double NetworkHashrate { get; init; }
    public double NetworkDifficulty { get; init; }
    public DateTime? LastNetworkBlockTime { get; init; }
    public long BlockHeight { get; init; }
    public int ConnectedPeers { get; init; }
    public int SharesPerSecond { get; init; }

    public DateTime Created { get; init; }
}
