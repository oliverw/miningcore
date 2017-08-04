using Newtonsoft.Json;

namespace MiningForce.Blockchain.Monero.StratumRequests
{
    public class MoneroGetJobRequest
	{
		[JsonProperty("id")]
		public string WorkerId { get; set; }
	}
}
