using Newtonsoft.Json;

namespace Miningcore.Blockchain.Conceal.DaemonRequests;

public class SplitIntegratedAddressRequest
{
    [JsonProperty("integrated_address")]
    public string WalletAddress { get; set; }
}