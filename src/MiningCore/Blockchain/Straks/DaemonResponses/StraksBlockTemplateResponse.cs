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

using Newtonsoft.Json;

namespace MiningCore.Blockchain.Straks.DaemonResponses
{
    public class StraksMasternode
    {
        public string Payee { get; set; }
        public string Script { get; set; }
        public long Amount { get; set; }
    }

    public class StraksCoinbaseTransaction
    {
        public string Data { get; set; }
        public string Hash { get; set; }
        //public string Txid { get; set; }
        public decimal Fee { get; set; }
        public int SigOps { get; set; }

        [JsonProperty("treasuryreward")]
        public ulong TreasuryReward { get; set; }
        public bool Required { get; set; }

        // "depends":[ ],
    }

    public class StraksBlockTemplate : Bitcoin.DaemonResponses.BlockTemplate
    {


        [JsonProperty("coinbasetxn")]
        public StraksCoinbaseTransaction CoinbaseTx { get; set; }

        public string Payee { get; set; }

        [JsonProperty("payee_amount")]
        public long? PayeeAmount { get; set; }

        public StraksMasternode Masternode { get; set; }

        [JsonProperty("masternode_payments")]
        public bool MasternodePaymentsStarted { get; set; }

        [JsonProperty("enforce_masternode_payments")]
        public bool MasternodePaymentsEnforced { get; set; }

    }
}
