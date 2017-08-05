using Newtonsoft.Json;

namespace MiningForce.Blockchain.Monero.DaemonResponses
{
    public class GetBlockHeaderResponse
	{
		public long Difficulty { get; set; }
		public long Depth { get; set; }
		public uint Height { get; set; }
		public string Hash { get; set; }
		public string Nonce { get; set; }
		public uint Reward { get; set; }
		public ulong Timestamp { get; set; }
		public string Status { get; set; }

		[JsonProperty("major_version")]
	    public string MajorVersion { get; set; }

		[JsonProperty("minor_version")]
		public string MinorVersion { get; set; }

		[JsonProperty("prev_hash")]
		public string PreviousBlockhash { get; set; }

		[JsonProperty("orphan_status")]
		public bool IsOrphaned { get; set; }
	}
}
