using System;
using System.Threading;
using System.Threading.Tasks;
using Miningcore.Blockchain;
using Miningcore.Configuration;
using Miningcore.Stratum;

namespace Miningcore.Mining
{
    public readonly struct ClientShare
    {
        public ClientShare(StratumConnection connection, Share share)
        {
            Connection = connection;
            Share = share;
        }

        public StratumConnection Connection { get; }
        public Share Share { get; }
    }

    public interface IMiningPool
    {
        PoolConfig Config { get; }
        PoolStats PoolStats { get; }
        BlockchainStats NetworkStats { get; }
        void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig);
        double HashrateFromShares(double shares, double interval);
        Task RunAsync(CancellationToken ct);
    }
}
