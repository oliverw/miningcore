using Miningcore.Configuration;

namespace Miningcore.Blockchain.Ethereum.Configuration;

public class EthereumPoolConfigExtra
{
    /// <summary>
    /// Base directory for generated DAGs
    /// </summary>
    public string DagDir { get; set; }

    /// <summary>
    /// Useful to specify the real chain type when running geth
    /// </summary>
    public string ChainTypeOverride { get; set; }

    /// <summary>
    /// getWork stream published via ZMQ
    /// </summary>
    public ZmqPubSubEndpointConfig BtStream { get; set; }
}
