using Newtonsoft.Json;

namespace Miningcore.Blockchain.Cryptonote.DaemonResponses;

public class SplitIntegratedAddressResponse
{
    [JsonProperty("standard_address")]
    public string StandardAddress { get; set; }

    public string Payment { get; set; }
}
