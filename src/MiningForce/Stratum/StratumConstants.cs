using System;
using System.Collections.Generic;
using System.Text;

namespace MiningForce.Stratum
{
    public class StratumConstants
    {
        /// <summary>
        /// Used to subscribe to work from a server, required before all other communication.
        /// </summary>
        public const string MsgSubscribe = "mining.subscribe";

        /// <summary>
        /// Used to authorize a worker, required before any shares can be submitted.
        /// </summary>
        public const string MsgAuthorize = "mining.authorize";

        /// <summary>
        /// Used to push new work to the miner.  Previous work should be aborted if Clean Jobs = true!
        /// </summary>
        public const string MsgMiningNotify = "mining.notify";

        /// <summary>
        /// Used to submit shares
        /// </summary>
        public const string MsgSubmitShare = "mining.submit";

        /// <summary>
        /// Used to signal the miner to stop submitting shares under the new difficulty.
        /// </summary>
        public const string MsgSetDifficulty = "mining.set_difficulty";

        public const string MsgGetTx = "mining.get_transactions";
    }
}
