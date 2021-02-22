/*
MiningCore 2.0
Copyright 2021 MinerNL (Miningcore.com)
*/

// using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Miningcore.Blockchain.Cryptonote.DaemonResponses
{

    internal class GetBlockTemplateRpcResponse : RpcResponse
    {
        [JsonProperty("result")]
        public GetBlockTemplateResponse Result { get; set; }
    }


    /// <summary>
    /// get_block_template<br />
    /// Alias: getblocktemplate
    /// <para>Get a block template on which mining a new block.</para>
    /// <para>Inputs:
    /// <br>  [wallet_address] - string - Address of wallet to receive coinbase transactions if block is successfully mined.</br>
    /// <br>  [reserve_size] - uint - Reserve size.</br></para>
    /// </summary>
    public class GetBlockTemplateResponse
    {
        /// <summary>
        /// Blob on which to try to mine a new block
        /// </summary>
        [JsonProperty("blocktemplate_blob")]
        public string Blob { get; set; }

        /// <summary>
        /// Blob on which to try to find a valid nonce
        /// </summary>
        [JsonProperty("blockhashing_blob")]
        public string HashBlob { get; set; }

        /// <summary>
        /// Difficulty of next block
        /// </summary>
        [JsonProperty("difficulty")]
        public long Difficulty { get; set; }

        /// <summary>
        /// Coinbase reward expected to be received if block is successfully mined
        /// </summary>
        [JsonProperty("expected_reward")]
        public ulong ExpectedReward { get; set; }

        /// <summary>
        /// Height on which to mine
        /// </summary>
        [JsonProperty("height")]
        public uint Height { get; set; }

        /// <summary>
        /// Hash of the most recent block on which to mine the next block
        /// </summary>
        [JsonProperty("prev_hash")]
        public string PreviousBlockhash { get; set; }

        /// <summary>
        /// Reserved offset
        /// </summary>
        [JsonProperty("reserved_offset")]
        public int ReservedOffset { get; set; }

        /// <summary>
        /// General RPC error code. "OK" means everything looks good
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// States if the result is obtained using the bootstrap mode, and is therefore not trusted (true), or when the daemon is fully synced (false)
        /// </summary>
        public bool Untrusted { get; set; }

        // Extra added:
        [JsonProperty("wide_difficulty")]
        public string WideDifficulty { get; set; }

        [JsonProperty("difficulty_top64")]
        public ulong DifficultyTop64 { get; set; }

        [JsonProperty("seed_height")]
        public ulong SeedHeight { get; set; }

        [JsonProperty("seed_hash")]
        public string SeedHash { get; set; }

        [JsonProperty("next_seed_hash")]
        public string NextSeedHash { get; set; }

        public override string ToString()
        {
            var typeInfo = typeof(GetBlockTemplateResponse);
            var nonNullPropertyList = typeInfo.GetProperties()
                                              .Where(p => p.GetValue(this) != default)
                                              .Select(p => $"{p.Name}: {p.GetValue(this)} ");
            return string.Join(Environment.NewLine, nonNullPropertyList);
        }

    }
}
