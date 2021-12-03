namespace Miningcore.Blockchain.Ergo.Configuration;

public class ErgoPaymentProcessingConfigExtra
{
    /// <summary>
    /// Password for unlocking wallet
    /// </summary>
    public string WalletPassword { get; set; }

    /// <summary>
    /// Minimum block confirmations
    /// </summary>
    public int? MinimumConfirmations { get; set; }
}
