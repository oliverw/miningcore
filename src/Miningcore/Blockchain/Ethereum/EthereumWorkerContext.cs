using Miningcore.Mining;

namespace Miningcore.Blockchain.Ethereum;

public class EthereumWorkerContext : WorkerContextBase
{
    /// <summary>
    /// Usually a wallet address
    /// </summary>
    public string Miner { get; set; }

    /// <summary>
    /// Arbitrary worker identififer for miners using multiple rigs
    /// </summary>
    public string Worker { get; set; }

    /// <summary>
    /// Stratum protocol version
    /// </summary>
    public int ProtocolVersion { get; set; }

    /// <summary>
    /// Unique value assigned per worker
    /// </summary>
    public string ExtraNonce1 { get; set; }
}
