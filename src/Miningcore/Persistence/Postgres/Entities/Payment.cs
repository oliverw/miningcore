namespace Miningcore.Persistence.Postgres.Entities;

public class Payment
{
    public long Id { get; set; }
    public string PoolId { get; set; }
    public string Coin { get; set; }
    public string Address { get; set; }
    public decimal Amount { get; set; }
    public string TransactionConfirmationData { get; set; }
    public DateTime Created { get; set; }
}
