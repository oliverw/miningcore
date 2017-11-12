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

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public object Id { get; set; }

        public TParam ParamsAs<TParam>() where TParam : class
        {
            if (Params is JToken)
                return ((JToken)Params)?.ToObject<TParam>();

            return (TParam)Params;
        }
    }
}
