using Miningcore.Configuration;

namespace Miningcore.Blockchain.Ethereum.Configuration;

public class EthereumPoolConfigExtra
{
    /// <summary>
    /// Useful to specify the real chain type when running geth
    /// </summary>
    public string ChainTypeOverride { get; set; }

    /// <summary>
    /// getWork stream published via ZMQ
    /// </summary>
    public ZmqPubSubEndpointConfig BtStream { get; set; }
}
