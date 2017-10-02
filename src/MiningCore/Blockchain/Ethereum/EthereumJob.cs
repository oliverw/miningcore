using System.Collections.Generic;
using System.Numerics;
using MiningCore.Stratum;

namespace MiningCore.Blockchain.Ethereum
{
    public class EthereumJob
    {
        public EthereumJob(ulong height, BigInteger diff)
        {
            BlockHeight = height;
            Difficulty = diff;
        }

        private readonly Dictionary<StratumClient<EthereumWorkerContext>, HashSet<ulong>> workerNonces = 
            new Dictionary<StratumClient<EthereumWorkerContext>, HashSet<ulong>>();

        public ulong BlockHeight { get; }
        public BigInteger Difficulty { get; }

        public void RegisterNonce(StratumClient<EthereumWorkerContext> worker, ulong nonce)
        {
            HashSet<ulong> nonces;

            if (!workerNonces.TryGetValue(worker, out nonces))
            {
                nonces = new HashSet<ulong>(new[] { nonce });
                workerNonces[worker] = nonces;
            }

            else
            {
                if (nonces.Contains(nonce))
                    throw new StratumException(StratumError.MinusOne, "duplicate share");

                nonces.Add(nonce);
            }
        }
    }
}
