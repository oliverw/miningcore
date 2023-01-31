using System.Text.Json.Serialization;

namespace Miningcore.Blockchain.Conceal.DaemonResponses;

public record GetInfoResponse
{
    /// <summary>
    /// Number of alternative blocks to main chain.
    /// </summary>
    [JsonPropertyName("alt_blocks_count")]
    public int AltBlocksCount { get; set; }
    
    /// <summary>
    /// ???
    /// </summary>
    [JsonPropertyName("block_major_version")]
    public int BlockMajorVersion { get; set; }
    
    /// <summary>
    /// ???
    /// </summary>
    [JsonPropertyName("block_minor_version")]
    public int BlockMinorVersion { get; set; }
    
    /// <summary>
    /// ???
    /// </summary>
    [JsonPropertyName("connections")]
    public string[] Connections { get; set; }
    
    /// <summary>
    /// Network difficulty(analogous to the strength of the network)
    /// </summary>
    [JsonPropertyName("difficulty")]
    public ulong Difficulty { get; set; }
    
    /// <summary>
    /// (Optional) Address dedicated to smartnode rewards
    /// </summary>
    [JsonPropertyName("fee_address")]
    public string FeeAddress { get; set; } = null;
    
    /// <summary>
    /// Total amount deposit
    /// </summary>
    [JsonPropertyName("full_deposit_amount")]
    public ulong FullAmountDeposit { get; set; }
    
    /// <summary>
    /// Grey Peerlist Size
    /// </summary>
    [JsonPropertyName("grey_peerlist_size")]
    public int GreyPeerlistSize { get; set; }
    
    /// <summary>
    /// The height of the next block in the chain.
    /// </summary>
    [JsonPropertyName("height")]
    public uint TargetHeight { get; set; }
    
    /// <summary>
    /// Number of peers connected to and pulling from your node.
    /// </summary>
    [JsonPropertyName("incoming_connections_count")]
    public int IncomingConnectionsCount { get; set; }
    
    /// <summary>
    /// Last block difficulty
    /// </summary>
    [JsonPropertyName("last_block_difficulty")]
    public ulong LastBlockDifficulty { get; set; }
    
    /// <summary>
    /// Last block reward
    /// </summary>
    [JsonPropertyName("last_block_reward")]
    public ulong LastBlockReward { get; set; }
    
    /// <summary>
    /// Last block timestamp
    /// </summary>
    [JsonPropertyName("last_block_timestamp")]
    public ulong LastBlockTimestamp { get; set; }
    
    /// <summary>
    /// Current length of longest chain known to daemon.
    /// </summary>
    [JsonPropertyName("last_known_block_index")]
    public uint Height { get; set; }
    
    /// <summary>
    /// Number of peers that you are connected to and getting information from.
    /// </summary>
    [JsonPropertyName("outgoing_connections_count")]
    public int OutgoingConnectionsCount { get; set; }
    
    /// <summary>
    /// General RPC error code. "OK" means everything looks good.
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// Hash of the highest block in the chain.
    /// </summary>
    [JsonPropertyName("top_block_hash")]
    public string TopBlockHash { get; set; }

    /// <summary>
    /// Total number of non-coinbase transaction in the chain.
    /// </summary>
    [JsonPropertyName("tx_count")]
    public uint TransactionCount { get; set; }

    /// <summary>
    /// Number of transactions that have been broadcast but not included in a block.
    /// </summary>
    [JsonPropertyName("tx_pool_size")]
    public uint TransactionPoolSize { get; set; }
    
    /// <summary>
    /// Version
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; }
    
    /// <summary>
    /// White Peerlist Size
    /// </summary>
    [JsonPropertyName("white_peerlist_size")]
    public uint WhitePeerlistSize { get; set; }
}