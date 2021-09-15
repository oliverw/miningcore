using System.Text.Json.Serialization;

namespace Miningcore.Nicehash.API
{
    public class NicehashMiningAlgorithm
    {
        public string Algorithm { get; set; }
        public string Title { get; set; }
        public bool Enabled { get; set; }
        public long Order { get; set; }
        public string DisplayMiningFactor { get; set; }
        public double MiningFactor { get; set; }
        public string DisplayMarketFactor { get; set; }
        public double MarketFactor { get; set; }
        public double MinimalOrderAmount { get; set; }
        public double MinSpeedLimit { get; set; }
        public double MaxSpeedLimit { get; set; }
        public double PriceDownStep { get; set; }
        public double MinimalPoolDifficulty { get; set; }
        public long Port { get; set; }
        public string Color { get; set; }
        public bool OrdersEnabled { get; set; }
    }

    public class NicehashMiningAlgorithmsResponse
    {
        [JsonPropertyName("miningAlgorithms")]
        public NicehashMiningAlgorithm[] Algorithms { get; set; }
    }
}
