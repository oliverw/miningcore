namespace Miningcore.Persistence.Postgres.Entities;

public class PoolStats
{
    public long Id { get; set; }
    public string PoolId { get; set; }

    public int ConnectedMiners { get; set; }
    public float PoolHashrate { get; set; }
    public double NetworkHashrate { get; set; }
    public double NetworkDifficulty { get; set; }
    public DateTime? LastNetworkBlockTime { get; set; }
    public long BlockHeight { get; set; }
    public int ConnectedPeers { get; set; }
    public int SharesPerSecond { get; set; }

    public DateTime Created { get; set; }
}
