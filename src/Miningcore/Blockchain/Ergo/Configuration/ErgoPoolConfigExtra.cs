using Miningcore.Configuration;

namespace Miningcore.Blockchain.Ergo.Configuration;

public class ErgoPoolConfigExtra
{
    /// <summary>
    /// Maximum number of tracked jobs.
    /// Default: 12 - you should increase this value if your blockrefreshinterval is higher than 300ms
    /// </summary>
    public int? MaxActiveJobs { get; set; }

    /// <summary>
    /// Blocktemplate stream published via ZMQ
    /// </summary>
    public ZmqPubSubEndpointConfig BtStream { get; set; }

    public int? ExtraNonce1Size { get; set; }
}
