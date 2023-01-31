using Newtonsoft.Json;

namespace Miningcore.Blockchain.Conceal.DaemonResponses;

public class GetBlockTemplateResponse
{
    [JsonProperty("blocktemplate_blob")]
    public string Blob { get; set; }

    public long Difficulty { get; set; }
    public uint Height { get; set; }

    [JsonProperty("reserved_offset")]
    public int ReservedOffset { get; set; }

    public string Status { get; set; }
}