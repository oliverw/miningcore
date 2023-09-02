using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.DaemonResponses
{
    public class FounderValue
    {
        public string Script { get; set; }
        public long Value { get; set; }
    }

    public class FounderValueBlockTemplateExtra
    {
        public JToken FounderValue { get; set; }

    }
}