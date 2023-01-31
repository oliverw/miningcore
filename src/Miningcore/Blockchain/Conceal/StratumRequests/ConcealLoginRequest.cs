using Newtonsoft.Json;

namespace Miningcore.Blockchain.Conceal.StratumRequests;

public class ConcealLoginRequest
{
    [JsonProperty("login")]
    public string Login { get; set; }

    [JsonProperty("pass")]
    public string Password { get; set; }

    [JsonProperty("agent")]
    public string UserAgent { get; set; }
}