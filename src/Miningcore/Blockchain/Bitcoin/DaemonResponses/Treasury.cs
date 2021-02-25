using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.DaemonResponses
{
    public class Treasury
    {
        public string Payee        { get; set; }
        public string ScriptPubkey { get; set; }
        public long   Amount       { get; set; }
    }

    public class TreasuryTemplateExtra
    {
        public JToken Treasury     { get; set; }
    }
}
