using Newtonsoft.Json;

namespace Miningcore.Blockchain.Bitcoin.DaemonResponses;

public class AddressInfo
{
    [JsonProperty("address")]
    public string Address { get; set; }

    [JsonProperty("scriptPubKey")]
    public string ScriptPubKey { get; set; }

    [JsonProperty("ismine")]
    public bool IsMine { get; set; }

    [JsonProperty("iswatchonly")]
    public bool IsWatchOnly { get; set; }

    [JsonProperty("isscript")]
    public bool IsScript { get; set; }

    [JsonProperty("ischange")]
    public bool IsChange { get; set; }

    [JsonProperty("iswitness")]
    public bool IsWitness { get; set; }
}
