using System.Numerics;
using Miningcore.Serialization;
using Newtonsoft.Json;

namespace Miningcore.Blockchain.Ethereum.DaemonResponses;

public class TransactionReceipt
{
    /// <summary>
    /// 32 Bytes - hash of the transaction.
    /// </summary>
    public string TransactionHash { get; set; }

    /// <summary>
    /// integer of the transactions index position in the block. null when its pending.
    /// </summary>
    [JsonProperty("transactionIndex")]
    [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
    public ulong Index { get; set; }

    /// <summary>
    /// 32 Bytes - hash of the block where this transaction was in. null when its pending.
    /// </summary>
    public string BlockHash { get; set; }

    /// <summary>
    /// block number where this transaction was in. null when its pending.
    /// </summary>
    [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
    public ulong BlockNumber { get; set; }

    /// <summary>
    /// The total amount of gas used when this transaction was executed in the block.
    /// </summary>
    [JsonConverter(typeof(HexToIntegralTypeJsonConverter<BigInteger>))]
    public BigInteger CummulativeGasUsed { get; set; }

    /// <summary>
    /// The amount of gas used by this specific transaction alone.
    /// </summary>
    [JsonConverter(typeof(HexToIntegralTypeJsonConverter<BigInteger>))]
    public BigInteger GasUsed { get; set; }

    /// <summary>
    /// The contract address created, if the transaction was a contract creation, otherwise null.
    /// </summary>
    public string ContractAddress { get; set; }
}
