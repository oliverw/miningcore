namespace Miningcore.Persistence.Model;

public record Balance
{
    public string PoolId { get; init; }
    public string Address { get; init; }

    /// <summary>
    /// Amount owed in pool-base-currency (ie. Bitcoin, not Satoshis)
    /// </summary>
    public decimal Amount { get; init; }

    public DateTime Created { get; init; }
    public DateTime Updated { get; init; }
}
