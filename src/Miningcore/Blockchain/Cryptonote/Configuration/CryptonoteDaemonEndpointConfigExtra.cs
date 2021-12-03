namespace Miningcore.Blockchain.Cryptonote.Configuration;

public class CryptonoteDaemonEndpointConfigExtra
{
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
