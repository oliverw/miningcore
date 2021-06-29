using Miningcore.Mining;

namespace Miningcore.Blockchain.Ethereum
{
    public class EthereumWorkerContext : WorkerContextBase
    {
        /// <summary>
        /// Usually a wallet address
        /// </summary>
        public string Miner { get; set; }

        /// <summary>
        /// Arbitrary worker identififer for miners using multiple rigs
        /// </summary>
        public string Worker { get; set; }

        public bool IsInitialWorkSent { get; set; } = false;

        public string ExtraNonce1 { get; set; }
    }
}
