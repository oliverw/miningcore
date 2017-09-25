using System;
using MiningCore.Configuration;
using MiningCore.Contracts;

namespace MiningCore.Blockchain.Ethereum
{
    public class EthereumJob
    {
        public EthereumJob(EthereumBlockTemplate blockTemplate, byte[] instanceId, string jobId,
            PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Contract.RequiresNonNull(instanceId, nameof(instanceId));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

            BlockTemplate = blockTemplate;
        }

        protected PoolConfig poolConfig;

        #region API-Surface

        public EthereumBlockTemplate BlockTemplate { get; private set; }

        public void Init()
        {
        }

        #endregion // API-Surface
    }
}
