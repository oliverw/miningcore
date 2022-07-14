namespace Miningcore.Notifications.Messages;

public record HashrateNotification
{
    public string PoolId { get; set; }
    public double Hashrate { get; set; }
    public string Miner { get; set; }
    public string Worker { get; set; }
}
