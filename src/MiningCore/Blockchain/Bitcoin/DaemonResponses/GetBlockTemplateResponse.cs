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

namespace MiningCore.Blockchain.Bitcoin.DaemonResponses
{
    public class BitcoinBlockTransaction
    {
        /// <summary>
        /// transaction data encoded in hexadecimal (byte-for-byte)
        /// </summary>
        public string Data { get; set; }

        /// <summary>
        /// transaction id encoded in little-endian hexadecimal
        /// </summary>
        public string TxId { get; set; }

        /// <summary>
        /// hash encoded in little-endian hexadecimal (including witness data)
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// The amount of the fee in BTC
        /// </summary>
        public decimal Fee { get; set; }
    }

    public class CoinbaseAux
    {
        public string Flags { get; set; }
    }

    public class BlockTemplate
    {
        /// <summary>
        /// The preferred block version
        /// </summary>
        public uint Version { get; set; }

        /// <summary>
        /// The hash of current highest block
        /// </summary>
        public string PreviousBlockhash { get; set; }

        /// <summary>
        /// Maximum allowable input to coinbase transaction, including the generation award and transaction fees (in Satoshis)
        /// </summary>
        public long CoinbaseValue { get; set; }

        /// <summary>
        /// The hash target
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// A range of valid nonces
        /// </summary>
        public string NonceRange { get; set; }

        /// <summary>
        /// Current timestamp in seconds since epoch (Jan 1 1970 GMT)
        /// </summary>
        public uint CurTime { get; set; }

        /// <summary>
        /// Compressed target of next block
        /// </summary>
        public string Bits { get; set; }

        /// <summary>
        /// The height of the next block
        /// </summary>
        public uint Height { get; set; }

        /// <summary>
        /// Contents of non-coinbase transactions that should be included in the next block
        /// </summary>
        public BitcoinBlockTransaction[] Transactions { get; set; }

        /// <summary>
        /// Data that should be included in the coinbase's scriptSig content
        /// </summary>
        public CoinbaseAux CoinbaseAux { get; set; }

        /// <summary>
        /// SegWit
        /// </summary>
        [JsonProperty("default_witness_commitment")]
        public string DefaultWitnessCommitment { get; set; }
    }
}
