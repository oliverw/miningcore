using MiningCore.JsonRpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiningCore.Blockchain.ZCash.DaemonResponses
{
    public class ZCashShieldingResponse
    {
        [JsonProperty("opid")]
        public string OperationId { get; set; }
    }
}
