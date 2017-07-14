using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace MiningCore.Protocols.JsonRpc
{
    [JsonObject(MemberSerialization.OptIn)]
    public class JsonRpcResponse
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, PropertyName = "jsonrpc")]
        public string JsonRpc => "2.0";

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, PropertyName = "result")]
        public object Result { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, PropertyName = "error")]
        public JsonRpcException Error { get; set; }

        [JsonProperty(PropertyName = "id")]
        public object Id { get; set; }
    }

    /// <summary>
    /// Represents a Json Rpc Response
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class JsonResponse<T>
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, PropertyName = "jsonrpc")]
        public string JsonRpc => "2.0";

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, PropertyName = "result")]
        public T Result { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, PropertyName = "error")]
        public JsonRpcException Error { get; set; }

        [JsonProperty(PropertyName = "id")]
        public object Id { get; set; }
    }
}
