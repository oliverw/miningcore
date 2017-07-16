using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiningCore.JsonRpc
{
    [JsonObject(MemberSerialization.OptIn)]
    public class JsonRpcResponse : JsonRpcResponse<object>
    {
        public JsonRpcResponse()
        {
        }

        public JsonRpcResponse(object result, string id = null) : base(result, id)
        {
        }

        public JsonRpcResponse(JsonRpcException ex, string id = null) : base(ex, id)
        {
        }
    }

    /// <summary>
    /// Represents a Json Rpc Response
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class JsonRpcResponse<T>
    {
        public JsonRpcResponse()
        {
        }

        public JsonRpcResponse(T result, string id = null)
        {
            Result = JToken.FromObject(result);
            Id = id;
        }

        public JsonRpcResponse(JsonRpcException ex, string id = null)
        {
            Error = ex;
            Id = id;
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, PropertyName = "jsonrpc")]
        public string JsonRpc => "2.0";

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, PropertyName = "result")]
        public JToken Result { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, PropertyName = "error")]
        public JsonRpcException Error { get; set; }

        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }
    }
}
