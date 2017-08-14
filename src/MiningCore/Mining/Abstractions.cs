using System;
using System.Threading.Tasks;
using MiningCore.Blockchain;
using MiningCore.Configuration;

namespace MiningCore.Mining
{
    public interface IMiningPool
    {
        IObservable<IShare> Shares { get; }
        PoolConfig Config { get; }
        PoolStats PoolStats { get; }
        BlockchainStats NetworkStats { get; }
        void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig);
        Task StartAsync();
    }
}
