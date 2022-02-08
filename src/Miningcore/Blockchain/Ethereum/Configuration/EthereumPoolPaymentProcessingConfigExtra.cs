namespace Miningcore.Blockchain.Ethereum.Configuration;

public class EthereumPoolPaymentProcessingConfigExtra
{
    /// <summary>
    /// Password of the daemons wallet (needed for processing payouts)
    /// </summary>
    public string CoinbasePassword { get; set; }

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
    /// Percentage as a decimal value between 0 and 100
    /// </summary>
    public decimal GasDeductionPercentage { get; set; }
    
    /// <summary>
    /// Max block reward
    /// </summary>
    public double MaxBlockReward { get; set; }

    /// <summary>
    /// maximum amount you’re willing to pay
    /// </summary>
    public ulong MaxFeePerGas { get; set; }

    /// <summary>
    /// Maximum gas price allowed for the transaction outside the specified time frame since last payout for the miner
    /// </summary>
    public ulong MaxGasLimit { get; set; }

    /// <summary>
    /// if True, miners pay payment tx fees
    /// </summary>
    public bool MinersPayTxFees { get; set; }

    /// <summary>
    /// No of transactions to spawn on each on-demand payout cycle
    /// </summary>
    public int PayoutBatchSize { get; set; }

    /// <summary>
    /// Hex encoded private key
    /// </summary>
    public string PrivateKey { get; set; }

    /// <summary>
    /// Percentage share miners receive as a decimal from 0 to 1
    /// </summary>
    public decimal RecipientShare { get; set; }
}
