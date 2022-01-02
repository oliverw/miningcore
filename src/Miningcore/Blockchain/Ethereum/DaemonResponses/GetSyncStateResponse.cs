using Miningcore.Serialization;
using Newtonsoft.Json;

namespace Miningcore.Blockchain.Ethereum.DaemonResponses;

public class SyncState
{
    /// <summary>
    /// The block at which the import started (will only be reset, after the sync reached his head)
    /// </summary>
    [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
    public ulong StartingBlock { get; set; }

    /// <summary>
    /// The current block, same as eth_blockNumber
    /// </summary>
    [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
    public ulong? CurrentBlock { get; set; }

    /// <summary>
    /// The estimated highest block
    /// </summary>
    [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
    public ulong? HighestBlock { get; set; }

    [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
    public ulong? KnownStates { get; set; }

    [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
    public ulong? PulledStates { get; set; }

    /// <summary>
    /// Parity: Total amount of snapshot chunks
    /// </summary>
    [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
    public ulong? WarpChunksAmount { get; set; }

    /// <summary>
    /// Parity: Total amount of snapshot chunks
    /// </summary>
    [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
    public ulong? WarpChunksProcessed { get; set; }
}
