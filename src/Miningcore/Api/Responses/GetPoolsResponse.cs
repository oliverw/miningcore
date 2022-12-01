using System.Text.Json.Serialization;
using Miningcore.Blockchain;
using Miningcore.Configuration;
using Miningcore.Mining;
using Newtonsoft.Json.Linq;

namespace Miningcore.Api.Responses;

public class ApiCoinConfig
{
    public string Type { get; set; }
    public string Name { get; set; }
    public string Symbol { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Website { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Market { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Family { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Algorithm { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Twitter { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Discord { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Telegram { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string CanonicalName { get; set; }
}

public class ApiPoolPaymentProcessingConfig
{
    public bool Enabled { get; set; }
    public decimal MinimumPayment { get; set; } // in pool-base-currency (ie. Bitcoin, not Satoshis)
    public string PayoutScheme { get; set; }
    public JToken PayoutSchemeConfig { get; set; }

    [Newtonsoft.Json.JsonExtensionData]
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
    public decimal TotalPaid { get; set; }
    public uint TotalBlocks { get; set; }
    public DateTime? LastPoolBlockTime { get; set; }
    public double PoolEffort { get; set; }
}

public class GetPoolsResponse
{
    public PoolInfo[] Pools { get; set; }
}
