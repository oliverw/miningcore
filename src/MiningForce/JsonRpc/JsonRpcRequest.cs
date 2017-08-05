using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiningForce.JsonRpc
{
    [JsonObject(MemberSerialization.OptIn)]
    public class JsonRpcRequest : JsonRpcRequest<object>
    {
        public JsonRpcRequest()
        {
        }

        public JsonRpcRequest(string method, object parameters, string id) : base(method, parameters, id)
        {
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class JsonRpcRequest<T>
    {
        public JsonRpcRequest()
        {
        }

        public JsonRpcRequest(string method, T parameters, string id)
        {
            Method = method;
            Params = parameters;
            Id = id;
        }

	    public T ParamsAs<T>() where T: class
	    {
		    return ((JToken) Params)?.ToObject<T>();
	    }

        [JsonProperty("jsonrpc")]
        public string JsonRpc => "2.0";

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public object Params { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }
    }
}
