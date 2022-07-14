namespace Miningcore.Blockchain.Ethereum.Configuration;

public class EthereumDaemonEndpointConfigExtra
{
    /// <summary>
    /// Optional port for streaming WebSocket data
    /// </summary>
    public int? PortWs { get; set; }

    /// <summary>
    /// Optional http-path for streaming WebSocket data
    /// </summary>
    public string HttpPathWs { get; set; }

    /// <summary>
    /// Optional: Use SSL to for daemon websocket streaming
    /// </summary>
    public bool SslWs { get; set; }

    /// <summary>
    /// Optional: port for getWork push-notifications
    /// (should match with geth --miner.notify or openethereum --notify-work)
    /// </summary>
    public string NotifyWorkUrl { get; set; }
}
