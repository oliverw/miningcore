using System.Text.Json.Serialization;

namespace Miningcore.Nicehash.API;

public record NicehashMiningAlgorithm
{
    public string Algorithm { get; init; }
    public string Title { get; init; }
    public bool Enabled { get; init; }
    public long Order { get; init; }
    public string DisplayMiningFactor { get; init; }
    public double MiningFactor { get; init; }
    public string DisplayMarketFactor { get; init; }
    public double MarketFactor { get; init; }
    public double MinimalOrderAmount { get; init; }
    public double MinSpeedLimit { get; init; }
    public double MaxSpeedLimit { get; init; }
    public double PriceDownStep { get; init; }
    public double MinimalPoolDifficulty { get; init; }
    public long Port { get; init; }
    public string Color { get; init; }
    public bool OrdersEnabled { get; init; }
}

public record NicehashMiningAlgorithmsResponse
{
    [JsonPropertyName("miningAlgorithms")]
    public NicehashMiningAlgorithm[] Algorithms { get; init; }
}
