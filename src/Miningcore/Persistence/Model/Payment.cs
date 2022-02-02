namespace Miningcore.Persistence.Model;

public record Payment
{
    public long Id { get; init; }
    public string PoolId { get; init; }
    public string Coin { get; init; }
    public string Address { get; init; }
    public decimal Amount { get; init; }
    public string TransactionConfirmationData { get; init; }
    public DateTime Created { get; init; }
}
