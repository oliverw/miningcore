using Newtonsoft.Json;

namespace MiningCore.Blockchain.Monero.DaemonResponses
{
    public class TransferResponse
	{
		/// <summary>
		/// Integer value of the fee charged for the txn (piconeros)
		/// </summary>
		public ulong Fee { get; set; }

		/// <summary>
		/// String for the transaction key if get_tx_key is true, otherwise, blank string.
		/// </summary>
		[JsonProperty("tx_key")]
		public string TxKey { get; set; }

		/// <summary>
		/// Publically searchable transaction hash
		/// </summary>
		[JsonProperty("tx_hash")]
		public string TxHash { get; set; }

		public string Status { get; set; }
	}
}
