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

using System.Numerics;
using MiningCore.Serialization;
using Newtonsoft.Json;

namespace MiningCore.Blockchain.Ethereum.DaemonRequests
{
    public class SendTransactionRequest
    {
        /// <summary>
        /// The address the transaction is send from.
        /// </summary>
        public string From { get; set; }

        /// <summary>
        /// The address the transaction is directed to.
        /// </summary>
        public string To { get; set; }

        /// <summary>
        /// (Optional) (default: 90000) Integer of the gas provided for the transaction execution. It will return unused gas
        /// </summary>
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ulong? Gas { get; set; }

        /// <summary>
        /// (Optional) Integer of the gas price used for each paid gas
        /// </summary>
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ulong? GasPrice { get; set; }

        /// <summary>
        /// (Optional) Integer of the value send with this transaction
        /// </summary>
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public BigInteger Value { get; set; }

        /// <summary>
        /// The compiled code of a contract OR the hash of the invoked method signature and encoded parameters.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Data { get; set; }
    }
}
