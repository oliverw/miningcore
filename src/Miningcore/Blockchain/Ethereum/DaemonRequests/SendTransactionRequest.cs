using System.Numerics;
using Miningcore.Serialization;
using Newtonsoft.Json;

namespace Miningcore.Blockchain.Ethereum.DaemonRequests
{
    public class SendTransactionRequest
    {
        /// <summary>
        /// The address the transaction is send from.
        /// </summary>
        public string From { get; set; }

        /// <summary>
        /// The address the transaction is directed to.
        /// </summary>
        public string To { get; set; }

        /// <summary>
        /// (Optional) (default: 90000) Integer of the gas provided for the transaction execution. It will return unused gas
        /// </summary>
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ulong? Gas { get; set; }

        /// <summary>
        /// (Optional) Integer of the gas price used for each paid gas
        /// </summary>
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ulong? GasPrice { get; set; }

        /// <summary>
        /// (Optional) Integer of the value send with this transaction
        /// </summary>
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public string Value { get; set; }

        /// <summary>
        /// The compiled code of a contract OR the hash of the invoked method signature and encoded parameters.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Data { get; set; }
    }
}
