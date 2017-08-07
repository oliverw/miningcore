using Newtonsoft.Json;

namespace MiningCore.Blockchain.Monero.DaemonRequests
{
	public class TransferDestination
	{
		public string Address { get; set; }
		public ulong Amount { get; set; }
	}

	public class TransferRequest
	{
	    public TransferDestination[] Destinations { get; set; }

		/// <summary>
		/// Number of outpouts from the blockchain to mix with (0 means no mixing)
		/// </summary>
		public uint Mixin { get; set; }

		/// <summary>
		/// (Optional) Random 32-byte/64-character hex string to identify a transaction
		/// </summary>
		[JsonProperty("payment_id")]
		public string PaymentId { get; set; }

		/// <summary>
		/// (Optional) Return the transaction key after sending
		/// </summary>
		[JsonProperty("get_tx_key")]
		public bool GetTxKey { get; set; }

		/// <summary>
		/// Number of blocks before the monero can be spent (0 to not add a lock)
		/// </summary>
		[JsonProperty("unlock_time")]
		public uint UnlockTime { get; set; }
	}
}
