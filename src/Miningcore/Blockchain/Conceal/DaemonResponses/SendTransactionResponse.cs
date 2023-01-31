using Newtonsoft.Json;

namespace Miningcore.Blockchain.Conceal.DaemonResponses;

public class SendTransactionResponse
{
    /// <summary>
    /// Publically searchable transaction hash
    /// </summary>
    [JsonProperty("transactionHash")]
    public string TxHash { get; set; }

    public string Status { get; set; }
}