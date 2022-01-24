namespace Miningcore.Persistence.Model;

public record Share
{
    public string PoolId { get; init; }
    public ulong BlockHeight { get; init; }
    public string Miner { get; init; }
    public string Worker { get; init; }
    public string UserAgent { get; init; }
    public double Difficulty { get; init; }
    public double NetworkDifficulty { get; init; }
    public string IpAddress { get; init; }
    public string Source { get; init; }
    public DateTime Created { get; init; }
}
