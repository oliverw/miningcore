using Newtonsoft.Json;

namespace MiningCore.Blockchain.Monero.StratumRequests
{
    public class MoneroSubmitShareRequest
    {
        [JsonProperty("id")]
        public string WorkerId { get; set; }

        [JsonProperty("job_id")]
        public string JobId { get; set; }

        public string Nonce { get; set; }

        [JsonProperty("result")]
        public string Hash { get; set; }
    }
}
