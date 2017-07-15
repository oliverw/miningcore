using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiningCore.JsonRpc
{
    [JsonObject(MemberSerialization.OptIn)]
    public class JsonRpcRequest : JsonRpcRequest<object>
    {
        public JsonRpcRequest()
        {
        }

        public JsonRpcRequest(string method, object pars, string id) : base(method, pars, id)
        {
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class JsonRpcRequest<T>
    {
        public JsonRpcRequest()
        {
        }

        public JsonRpcRequest(string method, T pars, string id)
        {
            Method = method;

            if(pars != null)
                Params = JObject.FromObject(pars);

            Id = id;
        }

        [JsonProperty("jsonrpc")]
        public string JsonRpc => "2.0";

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public JObject Params { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }
    }
}
