namespace Miningcore.Blockchain.Equihash.DaemonRequests;

public class ZSendManyRecipient
{
    public string Address { get; set; }
    public decimal Amount { get; set; }
    public string Memo { get; set; }
}
