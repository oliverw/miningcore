namespace Miningcore.Blockchain.Bitcoin.Configuration;

public class BitcoinDaemonEndpointConfigExtra
{
    public int? MinimumConfirmations { get; set; }

    /// <summary>
    /// Address of ZeroMQ block notify socket
    /// Should match the value of -zmqpubhashblock daemon start parameter
    /// </summary>
    public string ZmqBlockNotifySocket { get; set; }

    /// <summary>
    /// Optional: ZeroMQ block notify topic
    /// Defaults to "hashblock" if left blank
    /// </summary>
    public string ZmqBlockNotifyTopic { get; set; }
}
