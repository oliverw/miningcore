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

using System.Numerics;
using MiningCore.Serialization;
using Newtonsoft.Json;

namespace MiningCore.Blockchain.Ethereum.DaemonResponses
{
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
}
