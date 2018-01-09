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
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Mining;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiningCore.Api.Responses
{
    public class ApiCoinConfig
    {
        public string Type { get; set; }
    }

    public class ApiPoolPaymentProcessingConfig
    {
        public bool Enabled { get; set; }
        public decimal MinimumPayment { get; set; } // in pool-base-currency (ie. Bitcoin, not Satoshis)
        public string PayoutScheme { get; set; }
        public JToken PayoutSchemeConfig { get; set; }

        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }
    }

    public partial class PoolInfo
    {
        // Configuration Properties directly mapping to PoolConfig (omitting security relevant fields)
        public string Id { get; set; }

        public ApiCoinConfig Coin { get; set; }
        public Dictionary<int, PoolEndpoint> Ports { get; set; }
        public ApiPoolPaymentProcessingConfig PaymentProcessing { get; set; }
        public PoolShareBasedBanningConfig ShareBasedBanning { get; set; }
        public int ClientConnectionTimeout { get; set; }
        public int JobRebroadcastTimeout { get; set; }
        public int BlockRefreshInterval { get; set; }
        public float PoolFeePercent { get; set; }
        public string Address { get; set; }
        public string AddressInfoLink { get; set; }

        // Stats
        public PoolStats PoolStats { get; set; }
        public BlockchainStats NetworkStats { get; set; }
        public MinerPerformanceStats[] TopMiners { get; set; }
    }

    public class GetPoolsResponse
    {
        public PoolInfo[] Pools { get; set; }
    }
}
