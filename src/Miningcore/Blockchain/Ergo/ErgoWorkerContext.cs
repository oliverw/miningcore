using Miningcore.Mining;

namespace Miningcore.Blockchain.Ergo
{
    public class ErgoWorkerContext : WorkerContextBase
    {
        /// <summary>
        /// Usually a wallet address
        /// </summary>
        public string Miner { get; set; }

        /// <summary>
        /// Arbitrary worker identififer for miners using multiple rigs
        /// </summary>
        public string Worker { get; set; }

        public string ExtraNonce1 { get; set; }

        /// <summary>
        /// Effective adjusted diff
        /// </summary>
        public double EffectiveDifficulty { get; set; }

        /// <summary>
        /// Previous effective adjusted diff
        /// </summary>
        public double? PreviousEffectiveDifficulty { get; set; }
    }
}
