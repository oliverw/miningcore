using Newtonsoft.Json;

namespace Miningcore.Blockchain.Conceal.StratumRequests;

public class ConcealGetJobRequest
{
    [JsonProperty("id")]
    public string WorkerId { get; set; }
}