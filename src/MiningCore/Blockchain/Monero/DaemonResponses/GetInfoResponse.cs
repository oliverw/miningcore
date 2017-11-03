/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using Newtonsoft.Json;

namespace MiningCore.Blockchain.Monero.DaemonResponses
{
    public class GetInfoResponse
    {
        /// <summary>
        /// Current target for next proof of work.
        /// </summary>
        public uint Target { get; set; }

        /// <summary>
        /// The height of the next block in the chain.
        /// </summary>
        [JsonProperty("target_height")]
        public uint TargetHeight { get; set; }

        /// <summary>
        /// States if the node is on the testnet(true) or mainnet(false).
        /// </summary>
        [JsonProperty("testnet")]
        public bool IsTestnet { get; set; }

        /// <summary>
        /// Hash of the highest block in the chain.
        /// </summary>
        [JsonProperty("top_block_hash")]
        public string TopBlockHash { get; set; }

        /// <summary>
        /// Total number of non-coinbase transaction in the chain.
        /// </summary>
        [JsonProperty("tx_count")]
        public uint TransactionCount { get; set; }

        /// <summary>
        /// Number of transactions that have been broadcast but not included in a block.
        /// </summary>
        [JsonProperty("tx_pool_size")]
        public uint TransactionPoolSize { get; set; }

        /// <summary>
        /// Network difficulty(analogous to the strength of the network)
        /// </summary>
        public ulong Difficulty { get; set; }

        /// <summary>
        /// Current length of longest chain known to daemon.
        /// </summary>
        public uint Height { get; set; }

        /// <summary>
        /// General RPC error code. "OK" means everything looks good.
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Number of alternative blocks to main chain.
        /// </summary>
        [JsonProperty("alt_blocks_count")]
        public int AltBlocksCount { get; set; }

        /// <summary>
        /// Grey Peerlist Size
        /// </summary>
        [JsonProperty("grey_peerlist_size")]
        public int GreyPeerlistSize { get; set; }

        /// <summary>
        /// White Peerlist Size
        /// </summary>
        [JsonProperty("white_peerlist_size")]
        public uint WhitePeerlistSize { get; set; }

        /// <summary>
        /// Number of peers connected to and pulling from your node.
        /// </summary>
        [JsonProperty("incoming_connections_count")]
        public int IncomingConnectionsCount { get; set; }

        /// <summary>
        /// Number of peers that you are connected to and getting information from.
        /// </summary>
        [JsonProperty("outgoing_connections_count")]
        public int OutgoingConnectionsCount { get; set; }
    }
}
