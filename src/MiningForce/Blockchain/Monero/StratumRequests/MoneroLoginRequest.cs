using Newtonsoft.Json;

namespace MiningForce.Blockchain.Monero.StratumRequests
{
    public class MoneroLoginRequest
    {
		[JsonProperty("login")]
		public string Login { get; set; }

	    [JsonProperty("pass")]
	    public string Password { get; set; }

		[JsonProperty("agent")]
	    public string UserAgent { get; set; }
	}
}
