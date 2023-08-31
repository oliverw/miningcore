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
    /// There are several reports of bad actors taking advantage of the old "Ethash Stratum V1" protocol in order to perform multiple dangerous attacks like man-in-the-middle (MITM) attacks
    /// https://braiins.com/blog/hashrate-robbery-stratum-v2-fixes-this-and-more
    /// https://eips.ethereum.org/EIPS/eip-1571
    /// https://github.com/AndreaLanfranchi/EthereumStratum-2.0.0/issues/10#issuecomment-595053258
    /// Based on that critical fact, mining pool should be cautious of the risks of using a such deprecated and broken stratum protocol. Used it at your own risks.
    /// "Ethash Stratum V1" protocol is disabled by default
    /// </summary>
    public bool enableEthashStratumV1 { get; set; } = false;

    /// <summary>
    /// getWork stream published via ZMQ
    /// </summary>
    public ZmqPubSubEndpointConfig BtStream { get; set; }
}
