using Newtonsoft.Json;

namespace Miningcore.Blockchain.Cryptonote.DaemonResponses;

public class TransferSplitResponse
{
    /// <summary>
    /// List of tx fees charged for the txn (piconeros)
    /// </summary>
    [JsonProperty("fee_list")]
    public ulong[] FeeList { get; set; }

    /// <summary>
    /// Publically searchable transaction hash
    /// </summary>
    [JsonProperty("tx_hash_list")]
    public string[] TxHashList { get; set; }

    public string Status { get; set; }
}
