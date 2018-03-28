using MiningCore.JsonRpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiningCore.Blockchain.ZCash.DaemonResponses
{
    public class ZCashShieldingResponse
    {
        public string OperationId { get; set; }

        /// <summary>
        /// Number of coinbase utxos being shielded
        /// </summary>
        [JsonProperty("shieldedUTXOs")]
        public int ShieldedUtxOs { get; set; }

        /// <summary>
        /// Value of coinbase utxos being shielded
        /// </summary>
        [JsonProperty("shieldedValue")]
        public decimal ShieldedValue { get; set; }

        /// <summary>
        /// Number of coinbase utxos still available for shielding
        /// </summary>
        [JsonProperty("remainingUTXOs")]
        public int RemainingUtxOs { get; set; }

        /// <summary>
        /// Value of coinbase utxos still available for shielding
        /// </summary>
        [JsonProperty("remainingValue")]
        public decimal RemainingValue { get; set; }
    }
}
