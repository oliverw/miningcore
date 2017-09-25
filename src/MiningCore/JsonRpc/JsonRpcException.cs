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

using System;
using Newtonsoft.Json;

namespace MiningCore.JsonRpc
{
    /// <summary>
    /// 5.1 Error object
    /// When a rpc call encounters an error, the Response Object MUST contain the error member with a value that is a
    /// Object with the following members:
    /// codeA Number that indicates the error type that occurred.
    /// This MUST be an integer.messageA String providing a short description of the error.
    /// The message SHOULD be limited to a concise single sentence.dataA Primitive or Structured value that contains
    /// additional information about the error.
    /// This may be omitted.
    /// The value of this member is defined by the Server (e.g. detailed error information, nested errors etc.).
    /// The error codes from and including -32768 to -32000 are reserved for pre-defined errors. Any code within this
    /// range, but not defined explicitly below is reserved for future use. The error codes are nearly the same as those
    /// suggested for XML-RPC at the following url: http://xmlrpc-epi.sourceforge.net/specs/rfc.fault_codes.php
    /// code        message             meaning
    /// -32700      Parse error         Invalid JSON was received by the server.  An error occurred on the server while
    /// parsing the JSON text.
    /// -32600      Invalid Request     The JSON sent is not a valid Request object.
    /// -32601      Method not found    The method does not exist / is not available.
    /// -32602      Invalid params      Invalid method parameter(s).
    /// -32603      Internal error      Internal JSON-RPC error.
    /// -32000 to -32099 Server error   Reserved for implementation-defined server-errors.
    /// The remainder of the space is available for application defined errors.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class JsonRpcException
    {
        public JsonRpcException(int code, string message, object data, Exception inner = null)
        {
            Code = code;
            Message = message;
            Data = data;
            InnerException = inner;
        }

        [JsonProperty(PropertyName = "code")]
        public int Code { get; set; }

        [JsonProperty(PropertyName = "message")]
        public string Message { get; set; }

        [JsonProperty(PropertyName = "data")]
        public object Data { get; set; }

        [JsonIgnore]
        public Exception InnerException { get; set; }
    }
}
