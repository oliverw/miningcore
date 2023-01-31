using Newtonsoft.Json;

namespace Miningcore.Blockchain.Conceal.DaemonResponses;

public class SplitIntegratedAddressResponse
{
    [JsonProperty("address")]
    public string StandardAddress { get; set; }
    
    [JsonProperty("payment_id")]
    public string Payment { get; set; }
}