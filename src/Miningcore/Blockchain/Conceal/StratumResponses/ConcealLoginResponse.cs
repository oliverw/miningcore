using Newtonsoft.Json;

namespace Miningcore.Blockchain.Conceal.StratumResponses;

public class ConcealJobParams
{
    [JsonProperty("job_id")]
    public string JobId { get; set; }

    public string Blob { get; set; }
    public string Target { get; set; }

    [JsonProperty("algo")]
    public string Algorithm { get; set; }

    /// <summary>
    /// Introduced for CNv4 (aka CryptonightR)
    /// </summary>
    public ulong Height { get; set; }
}

public class ConcealLoginResponse : ConcealResponseBase
{
    public string Id { get; set; } = "1";
    public ConcealJobParams Job { get; set; }
}