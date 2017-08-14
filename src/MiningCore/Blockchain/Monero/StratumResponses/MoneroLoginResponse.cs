using Newtonsoft.Json;

namespace MiningCore.Blockchain.Monero.StratumResponses
{
    public class MoneroJobParams
    {
        [JsonProperty("job_id")]
        public string JobId { get; set; }

        public string Blob { get; set; }
        public string Target { get; set; }
    }

    public class MoneroLoginResponse : MoneroResponseBase
    {
        public string Id { get; set; }
        public MoneroJobParams Job { get; set; }
    }
}
