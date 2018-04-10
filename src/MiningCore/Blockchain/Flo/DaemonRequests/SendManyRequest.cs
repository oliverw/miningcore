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

using System.Collections.Generic;
using Newtonsoft.Json;

namespace MiningCore.Blockchain.Flo.DaemonRequests
{
    public class SendManyRequest
    {
        /// <summary>
        /// (string, required) DEPRECATED. The account to send the funds from. Should be "" for the default account
        /// </summary>
        [JsonProperty("fromaccount", NullValueHandling = NullValueHandling.Ignore)]
        public string FromAccount { get; set; }

        /// <summary>
        /// (string, required) A json object with addresses and amounts
        /// (numeric or string) The flo address is the key, the numeric amount (can be string) in Flo is the value\n
        /// </summary>
        [JsonProperty("amounts", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, decimal> Amounts { get; set; }

        /// <summary>
        /// (numeric, optional, default=1) Only use the balance confirmed at least this many times.
        /// </summary>
        [JsonProperty("minconf", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? MinConf { get; set; }

        /// <summary>
        /// (string, optional) A comment
        /// </summary>
        [JsonProperty("comment", NullValueHandling = NullValueHandling.Ignore)]
        public string Comment { get; set; }

        /// <summary>
        /// (array, optional) A json array with addresses.
        /// The fee will be equally deducted from the amount of each selected address.
        /// Those recipients will receive less flos than you enter in their corresponding amount field.
        /// If no addresses are specified here, the sender pays the fee.
        /// (string) Subtract fee from this address
        /// </summary>
        [JsonProperty("subtractfeefrom", NullValueHandling = NullValueHandling.Ignore)]
        public string[] SubtractFeeFrom { get; set; }

        /// <summary>
        /// (boolean, optional) Allow this transaction to be replaced by a transaction with higher fees via BIP 125
        /// </summary>
        [JsonProperty("replaceable", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Replaceable { get; set; }

        /// <summary>
        /// (numeric, optional) Confirmation target (in blocks)
        /// </summary>
        [JsonProperty("conf_target", NullValueHandling = NullValueHandling.Ignore)]
        public int? ConfTarget { get; set; }

        /// <summary>
        /// (string, optional, default=UNSET) The fee estimate mode, must be one of:
        /// UNSET
        /// ECONOMICAL
        /// CONSERVATIVE
        /// </summary>
        [JsonProperty("estimate_mod", NullValueHandling = NullValueHandling.Ignore)]
        public string EstimateMode { get; set; }

        /// <summary>
        /// (string, optional) FLO data field (default = "")
        /// </summary>
        [JsonProperty("floData", NullValueHandling = NullValueHandling.Ignore)]
        public string FloData { get; set; }
    }
}