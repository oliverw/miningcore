using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Miningcore.JsonRpc;

[JsonObject(MemberSerialization.OptIn)]
public class JsonRpcRequest : JsonRpcRequest<object>
{
    public JsonRpcRequest()
    {
    }

    public JsonRpcRequest(string method, object parameters, object id) : base(method, parameters, id)
    {
    }
}

[JsonObject(MemberSerialization.OptIn)]
public class JsonRpcRequest<T>
{
    public JsonRpcRequest()
    {
    }

    public JsonRpcRequest(string method, T parameters, object id)
    {
        Method = method;
        Params = parameters;
        Id = id;
    }

    [JsonProperty("jsonrpc")]
    public string JsonRpc => "2.0";

    [JsonProperty("method", NullValueHandling = NullValueHandling.Ignore)]
    public string Method { get; set; }

    [JsonProperty("params")]
    public object Params { get; set; }

    [JsonProperty("id")]
    public object Id { get; set; }

    [JsonExtensionData]
    public IDictionary<string, object> Extra { get; set; }

    public TParam ParamsAs<TParam>() where TParam : class
    {
        if(Params is JToken token)
            return token.ToObject<TParam>();

        return (TParam) Params;
    }
}
