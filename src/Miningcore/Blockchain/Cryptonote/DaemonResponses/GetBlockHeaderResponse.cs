/*
MiningCore 2.0
Copyright 2021 MinerNL (Miningcore.com)
*/

using Newtonsoft.Json;

namespace Miningcore.Blockchain.Cryptonote.DaemonResponses
{
    /// <summary>
    /// get_last_block_header<br />
    /// Alias: getlastblockheader
    /// <para>Block header information for the most recent block is easily retrieved with this method.</para>
    /// <para>Inputs:
    /// <br>  No inputs are needed</br>
    /// </para>
    /// </summary>
    public class GetBlockHeaderResponse
    {
        /// <summary>
        ///  A structure containing block header information
        /// </summary>
        [JsonProperty("block_header")]
        public BlockHeader BlockHeader { get; set; }

        /// <summary>
        /// General RPC error code. "OK" means everything looks good
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// States if the result is obtained using the bootstrap mode, and is therefore not trusted (true), or when the daemon is fully synced (false)
        /// </summary>
        public bool Untrusted { get; set; }
    }

    public class BlockHeader
    {
        /// <summary>
        /// The block size in bytes
        /// </summary>
        [JsonProperty("block_size")]
        public uint BlockSize {get; set;}

        /// <summary>
        /// The number of blocks succeeding this block on the blockchain. A larger number means an older block
        /// </summary>
        public long Depth { get; set; }

        /// <summary>
        /// The strength of the Monero network based on mining power
        /// </summary>
        public uint Difficulty { get; set; }

        /// <summary>
        /// The hash of this block
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// The number of blocks preceding this block on the blockchain
        /// </summary>
        public uint Height { get; set; }

        /// <summary>
        /// The major version of the monero protocol at this block height
        /// </summary>
        [JsonProperty("major_version")]
        public uint MajorVersion { get; set; }

        /// <summary>
        /// The minor version of the monero protocol at this block height
        /// </summary>
        [JsonProperty("minor_version")]
        public uint MinorVersion { get; set; }

        /// <summary>
        /// a cryptographic random one-time number used in mining a Monero block
        /// </summary>
        public string Nonce { get; set; }

        /// <summary>
        /// Number of transactions in the block, not counting the coinbase tx
        /// </summary>
        [JsonProperty("num_txes")]
        public uint TxCount { get; set; }

        /// <summary>
        /// Usually false. If true, this block is not part of the longest chain
        /// </summary>
        [JsonProperty("orphan_status")]
        public bool IsOrphaned { get; set; }

        /// <summary>
        /// The hash of the block immediately preceding this block in the chain
        /// </summary>
        [JsonProperty("prev_hash")]
        public string PreviousBlockhash { get; set; }

        /// <summary>
        ///  The amount of new atomic units generated in this block and rewarded to the miner. Note: 1 XMR = 1e12 atomic units
        /// </summary>
        public ulong Reward { get; set; }

        /// <summary>
        /// The unix time at which the block was recorded into the blockchain
        /// </summary>
        public ulong Timestamp { get; set; }

    }

}
