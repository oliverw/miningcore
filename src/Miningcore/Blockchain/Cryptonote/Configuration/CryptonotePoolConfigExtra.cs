using Miningcore.Configuration;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Cryptonote.Configuration;

public class CryptonotePoolConfigExtra
{
    /// <summary>
    /// Blocktemplate stream published via ZMQ
    /// </summary>
    public ZmqPubSubEndpointConfig BtStream { get; set; }

    /// <summary>
    /// RandomX virtual machine bucket
    /// Defaults to poolId if not specified
    /// </summary>
    public string RandomXRealm { get; set; }

    /// <summary>
    /// Optional override value for RandomX VM Flags (see Native/LibRandomX.cs)
    /// </summary>
    public JToken RandomXFlagsOverride { get; set; }

    /// <summary>
    /// Optional additive value for RandomX VM Flags (see Native/LibRandomX.cs)
    /// </summary>
    public JToken RandomXFlagsAdd { get; set; }

    /// <summary>
    /// Optional value for number of RandomX VMs allocated per generation (new seed hash)
    /// Set to -1 to scale to number of cores
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public int RandomXVMCount { get; set; } = 1;
}
