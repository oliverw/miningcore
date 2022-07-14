using Newtonsoft.Json;

namespace Miningcore.Blockchain.Equihash.DaemonResponses;

public class ZCashShieldingResponse
{
    [JsonProperty("opid")]
    public string OperationId { get; set; }
}
