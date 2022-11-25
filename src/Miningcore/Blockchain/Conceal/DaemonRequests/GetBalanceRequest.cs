using Newtonsoft.Json;

namespace Miningcore.Blockchain.Conceal.DaemonRequests;

public class GetBalanceRequest
{
    /// <summary>
    /// (Optional) If address is not specified, returns the balance of the first address in the wallet.
    /// </summary>
    [JsonProperty("address")]
    public string Address { get; set; }
}