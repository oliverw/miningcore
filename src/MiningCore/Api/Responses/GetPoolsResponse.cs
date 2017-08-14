using System.Collections.Generic;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Mining;

namespace MiningCore.Api.Responses
{
    public class PoolInfo
    {
        // Configuration Properties directly mapping to PoolConfig (omitting security relevant fields)
        public string Id { get; set; }

        public CoinConfig Coin { get; set; }
        public Dictionary<int, PoolEndpoint> Ports { get; set; }
        public PoolPaymentProcessingConfig PaymentProcessing { get; set; }
        public PoolBanningConfig Banning { get; set; }
        public int ClientConnectionTimeout { get; set; }
        public int JobRebroadcastTimeout { get; set; }
        public int BlockRefreshInterval { get; set; }

        // Stats
        public PoolStats PoolStats { get; set; }

        public BlockchainStats NetworkStats { get; set; }
    }

    public class GetPoolsResponse
    {
        public PoolInfo[] Pools { get; set; }
    }
}