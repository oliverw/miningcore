namespace Miningcore.Api.Responses;

public class Block
{
    public string PoolId { get; set; }
    public ulong BlockHeight { get; set; }
    public double NetworkDifficulty { get; set; }
    public string Status { get; set; }
    public string Type { get; set; }
    public double ConfirmationProgress { get; set; }
    public double? Effort { get; set; }
    public string TransactionConfirmationData { get; set; }
    public decimal Reward { get; set; }
    public string InfoLink { get; set; }
    public string Hash { get; set; }
    public string Miner { get; set; }
    public string Source { get; set; }
    public DateTime Created { get; set; }
}
