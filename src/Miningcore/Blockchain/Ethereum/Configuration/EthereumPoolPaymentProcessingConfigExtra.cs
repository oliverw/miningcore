namespace Miningcore.Blockchain.Ethereum.Configuration;

public class EthereumPoolPaymentProcessingConfigExtra
{
    /// <summary>
    /// True to exempt transaction fees from miner rewards
    /// </summary>
    public bool KeepTransactionFees { get; set; }

    /// <summary>
    /// True to exempt uncle rewards from miner rewards
    /// </summary>
    public bool KeepUncles { get; set; }

    /// <summary>
    /// Gas amount for payout tx (advanced users only)
    /// </summary>
    public ulong Gas { get; set; }

    /// <summary>
    /// maximum amount you’re willing to pay
    /// </summary>
    public ulong MaxFeePerGas { get; set; }

    /// <summary>
    /// Search offset to start looking for uncles
    /// </summary>
    public uint BlockSearchOffset { get; set; } = 50;
}
