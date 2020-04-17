using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.DaemonResponses
{
    public class Masternode
    {
        public string Payee { get; set; }
        public string Script { get; set; }
        public long Amount { get; set; }
    }

    public class SuperBlock
    {
        public string Payee { get; set; }
        public long Amount { get; set; }
    }

    public class MasterNodeBlockTemplateExtra : PayeeBlockTemplateExtra
    {
        public JToken Masternode { get; set; }

        [JsonProperty("indexnode")]
        private JToken Indexnode { set { Masternode = value; } }

        [JsonProperty("masternode_payments_started")]
        public bool MasternodePaymentsStarted { get; set; }

        [JsonProperty("masternode_payments_enforced")]
        public bool MasternodePaymentsEnforced { get; set; }

        [JsonProperty("superblock")]
        public SuperBlock[] SuperBlocks { get; set; }

        [JsonProperty("superblocks_started")]
        public bool SuperblocksStarted { get; set; }

        [JsonProperty("superblocks_enabled")]
        public bool SuperblocksEnabled { get; set; }

        [JsonProperty("coinbase_payload")]
        public string CoinbasePayload { get; set; }
    }
}
