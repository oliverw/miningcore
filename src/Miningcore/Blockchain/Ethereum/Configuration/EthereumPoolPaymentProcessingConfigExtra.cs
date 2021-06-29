namespace Miningcore.Blockchain.Ethereum.Configuration
{
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
    }
}
