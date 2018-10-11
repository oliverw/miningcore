using Miningcore.JsonRpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Equihash.DaemonResponses
{
    public class ZCashShieldingResponse
    {
        [JsonProperty("opid")]
        public string OperationId { get; set; }
    }
}
