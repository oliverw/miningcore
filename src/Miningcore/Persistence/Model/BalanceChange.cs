namespace Miningcore.Persistence.Model;

public record BalanceChange
{
    public long Id { get; init; }
    public string PoolId { get; init; }
    public string Address { get; init; }

    /// <summary>
    /// Amount owed in pool-base-currency (ie. Bitcoin, not Satoshis)
    /// </summary>
    public decimal Amount { get; init; }

    public string Usage { get; init; }

    public DateTime Created { get; init; }
}
