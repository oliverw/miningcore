using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Miningcore.JsonRpc;

[JsonObject(MemberSerialization.OptIn)]
public class JsonRpcResponse : JsonRpcResponse<object>
{
    public JsonRpcResponse()
    {
    }

    public JsonRpcResponse(object result, object id = null) : base(result, id)
    {
    }

    public JsonRpcResponse(JsonRpcError ex, object id = null, object result = null) : base(ex, id, result)
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

    public JsonRpcResponse(T result, object id = null)
    {
        Result = result;
        Id = id;
    }

    public JsonRpcResponse(JsonRpcError ex, object id, object result)
    {
        Error = ex;
        Id = id;

        if(result != null)
            Result = JToken.FromObject(result);
    }

    //[JsonProperty(PropertyName = "jsonrpc")]
    //public string JsonRpc => "2.0";

    [JsonProperty(PropertyName = "result", NullValueHandling = NullValueHandling.Ignore)]
    public object Result { get; set; }

    [JsonProperty(PropertyName = "error", NullValueHandling = NullValueHandling.Ignore)]
    public JsonRpcError Error { get; set; }

    [JsonProperty(PropertyName = "id", NullValueHandling = NullValueHandling.Ignore)]
    public object Id { get; set; }

    [JsonExtensionData]
    public IDictionary<string, object> Extra { get; set; }

    public TParam ResultAs<TParam>() where TParam : class
    {
        if(Result is JToken token)
            return token.ToObject<TParam>();

        return (TParam) Result;
    }
}
