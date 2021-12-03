using Newtonsoft.Json;

namespace Miningcore.Blockchain.Cryptonote.DaemonResponses;

public class BlockHeader
{
    public long Difficulty { get; set; }
    public long Depth { get; set; }
    public uint Height { get; set; }
    public string Hash { get; set; }
    public string Nonce { get; set; }
    public ulong Reward { get; set; }
    public ulong Timestamp { get; set; }

    [JsonProperty("major_version")]
    public uint MajorVersion { get; set; }

    [JsonProperty("minor_version")]
    public uint MinorVersion { get; set; }

    [JsonProperty("prev_hash")]
    public string PreviousBlockhash { get; set; }

    [JsonProperty("orphan_status")]
    public bool IsOrphaned { get; set; }
}

public class GetBlockHeaderResponse
{
    [JsonProperty("block_header")]
    public BlockHeader BlockHeader { get; set; }

    public string Status { get; set; }
}
