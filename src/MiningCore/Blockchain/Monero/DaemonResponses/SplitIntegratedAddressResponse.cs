using Newtonsoft.Json;

namespace MiningCore.Blockchain.Monero.DaemonResponses
{
    public class SplitIntegratedAddressResponse
    {
        [JsonProperty("standard_address")]
        public string StandardAddress { get; set; }

        public string Payment { get; set; }
    }
}
