namespace Miningcore.Api.Responses;

public class Payment
{
    public string Coin { get; set; }
    public string Address { get; set; }
    public string AddressInfoLink { get; set; }
    public decimal Amount { get; set; }
    public string TransactionConfirmationData { get; set; }
    public string TransactionInfoLink { get; set; }
    public DateTime Created { get; set; }
}
