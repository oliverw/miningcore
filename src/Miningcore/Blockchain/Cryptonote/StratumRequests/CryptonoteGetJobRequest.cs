using Newtonsoft.Json;

namespace Miningcore.Blockchain.Cryptonote.StratumRequests;

public class CryptonoteGetJobRequest
{
    [JsonProperty("id")]
    public string WorkerId { get; set; }
}
