using Newtonsoft.Json;

namespace Miningcore.Blockchain.Cryptonote.DaemonResponses;

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

    /// <summary>
    /// Raw transaction represented as hex string, if get_tx_hex is true.
    /// </summary>
    [JsonProperty("tx_blob")]
    public string TxBlob { get; set; }

    /// <summary>
    /// (Optional) If true, the newly created transaction will not be relayed to the monero network
    /// </summary>
    [JsonProperty("do_not_relay")]
    public string DoNotRelay { get; set; }

    public string Status { get; set; }
}
