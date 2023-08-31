using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.DaemonResponses
{
    public class FounderReward
    {
        public string FounderPayee { get; set; }
        public long FounderAmount { get; set; }
    }

    public class FounderRewardBlockTemplateExtra
    {
        public JToken FounderReward { get; set; }

        [JsonProperty("founder_reward_enforced")]
        public bool FounderRewardEnforced { get; set; }
    }
}