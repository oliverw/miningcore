using Miningcore.Mining;

namespace Miningcore.Blockchain.Bitcoin;

public class BitcoinWorkerContext : WorkerContextBase
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
    /// Unique value assigned per worker
    /// </summary>
    public string ExtraNonce1 { get; set; }

    /// <summary>
    /// Mask for version-rolling (Overt ASIC-Boost)
    /// </summary>
    public uint? VersionRollingMask { get; internal set; }
}
