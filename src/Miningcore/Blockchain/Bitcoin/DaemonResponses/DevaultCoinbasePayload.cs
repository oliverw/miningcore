using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.DaemonResponses
{
    public class CoinbasePayload
    {
        public string Payee { get; set; }
        public string Script { get; set; }
        public long Amount { get; set; }
    }

    public class CoinbasePayloadBlockTemplateExtra : PayeeBlockTemplateExtra
    {   
        [JsonProperty("coinbase_payload")]
        public JToken CoinbasePayload { get; set; }
    }

}
