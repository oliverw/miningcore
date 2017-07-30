using Newtonsoft.Json;

namespace MiningForce.Blockchain.Monero.DaemonRequests
{
    public class GetBlockTemplateRequest
    {
		/// <summary>
		/// Address of wallet to receive coinbase transactions if block is successfully mined.
		/// </summary>
		[JsonProperty("wallet_address ")]
		public string WalletAddress { get; set; }

	    [JsonProperty("reserved_offset")]
	    public uint ReservedOffset { get; set; }
	}
}
