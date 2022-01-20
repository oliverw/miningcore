namespace Miningcore.Persistence.Model;

public class Balance
{
    public string PoolId { get; set; }
    public string Address { get; set; }

    /// <summary>
    /// Amount owed in pool-base-currency (ie. Bitcoin, not Satoshis)
    /// </summary>
    public decimal Amount { get; set; }

    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    /// <summary>
    /// Last paid date used to decide on gas price
    /// </summary>
    public DateTime? PaidDate { get; set; }
}
