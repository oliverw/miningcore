using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Stratum
{
    public class StratumConstants
    {
        public const string MsgSubscribe = "mining.subscribe";
        public const string MsgAuthorize = "mining.authorize";
        public const string MsgSubmitShare = "mining.submit";
        public const string MsgGetTx = "mining.get_transactions";

        public const string MsgSetDifficulty = "mining.set_difficulty";
        public const string MsgMiningNotify = "mining.notify";
    }
}
