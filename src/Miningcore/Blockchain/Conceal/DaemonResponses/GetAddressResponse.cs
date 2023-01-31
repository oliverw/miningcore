using Newtonsoft.Json;

namespace Miningcore.Blockchain.Conceal.DaemonResponses;

public class GetAddressResponse
{
    [JsonProperty("addresses")]
    public string[] Address { get; set; }
}