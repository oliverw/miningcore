using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace MiningCore.Protocols.JsonRpc
{
    [JsonObject(MemberSerialization.OptIn)]
    public class JsonRpcRequest
    {
        public JsonRpcRequest()
        {
        }

        public JsonRpcRequest(string method, object pars, object id)
        {
            Method = method;
            Params = pars;
            Id = id;
        }

        [JsonProperty("jsonrpc")]
        public string JsonRpc => "2.0";

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public object Params { get; set; }

        [JsonProperty("id")]
        public object Id { get; set; }
    }
}
