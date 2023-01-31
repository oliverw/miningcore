using Newtonsoft.Json;

namespace Miningcore.Blockchain.Conceal.DaemonResponses;

public class GetBalanceResponse
{
    /// <summary>
    /// Available balance of the specified address
    /// </summary>
    [JsonProperty("availableBalance")]
    public decimal Balance { get; set; }
    
    /// <summary>
    /// Locked amount of the specified address
    /// </summary>
    [JsonProperty("lockedAmount")]
    public decimal LockedBalance { get; set; }
    
    /// <summary>
    /// Locked amount of the specified address
    /// </summary>
    [JsonProperty("lockedDepositBalance")]
    public decimal LockedDepositBalance { get; set; }
    
    /// <summary>
    /// Balance of unlocked deposits that can be withdrawn
    /// </summary>
    [JsonProperty("unlockedDepositBalance")]
    public decimal UnlockedDepositBalance { get; set; }
}