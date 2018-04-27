/* 
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial 
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

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

        public JsonRpcResponse(object result, object id = null) : base(result, id)
        {
        }

        public JsonRpcResponse(JsonRpcException ex, object id = null, object result = null) : base(ex, id, result)
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

        public JsonRpcResponse(JsonRpcException ex, object id, object result)
        {
            Error = ex;
            Id = id;

            if (result != null)
                Result = JToken.FromObject(result);
        }

        //[JsonProperty(PropertyName = "jsonrpc")]
        //public string JsonRpc => "2.0";

        [JsonProperty(PropertyName = "result", NullValueHandling = NullValueHandling.Ignore)]
        public object Result { get; set; }

        [JsonProperty(PropertyName = "error")]
        public JsonRpcException Error { get; set; }

        [JsonProperty(PropertyName = "id", NullValueHandling = NullValueHandling.Ignore)]
        public object Id { get; set; }

        public TParam ResultAs<TParam>() where TParam : class
        {
            if (Result is JToken)
                return ((JToken)Result)?.ToObject<TParam>();

            return (TParam)Result;
        }
    }
}
