using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Equihash.Configuration;

public class EquihashPoolConfigExtra
{
    /// <summary>
    /// z-addr holding pool funds - required for payment processing
    /// </summary>
    [JsonProperty("z-address")]
    public string ZAddress { get; set; }

    /// <summary>
    /// Custom Arguments for getblocktemplate RPC
    /// </summary>
    public JToken GBTArgs { get; set; }
}
