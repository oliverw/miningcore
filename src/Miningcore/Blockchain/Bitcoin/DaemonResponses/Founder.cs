using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.DaemonResponses
{
    public class Founder
    {
        public string Payee { get; set; }
        public string Script { get; set; }
        public long Amount { get; set; }
    }

    public class FounderBlockTemplateExtra
    {
        public JToken Founder { get; set; }

        [JsonProperty("founder_payments_started")]
        public bool FounderPaymentsStarted { get; set; }
    }
}
