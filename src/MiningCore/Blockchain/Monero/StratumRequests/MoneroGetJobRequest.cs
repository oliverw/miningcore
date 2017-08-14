using Newtonsoft.Json;

namespace MiningCore.Blockchain.Monero.StratumRequests
{
    public class MoneroGetJobRequest
    {
        [JsonProperty("id")]
        public string WorkerId { get; set; }
    }
}