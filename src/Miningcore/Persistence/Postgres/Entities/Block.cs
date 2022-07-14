namespace Miningcore.Persistence.Postgres.Entities;

public class Block
{
    public long Id { get; set; }
    public string PoolId { get; set; }
    public long BlockHeight { get; set; }
    public double NetworkDifficulty { get; set; }
    public string Status { get; set; }
    public string Type { get; set; }
    public double ConfirmationProgress { get; set; }
    public double? Effort { get; set; }
    public string TransactionConfirmationData { get; set; }
    public string Miner { get; set; }
    public decimal Reward { get; set; }
    public string Source { get; set; }
    public string Hash { get; set; }
    public DateTime Created { get; set; }
}
