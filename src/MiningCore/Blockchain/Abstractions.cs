using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MiningCore.Configuration;
using MiningCore.JsonRpc;
using MiningCore.Stratum;
using Newtonsoft.Json.Linq;

namespace MiningCore.Blockchain
{
    public class NetworkStats
    {
        public double HashRate { get; set; }
        public DateTime LastBlockTime { get; set; }
        public double Difficulty { get; set; }
        public int BlockHeight { get; set; }
        public int ConnectedPeers { get; set; }
        public string RewardType { get; set; }
    }

    public interface IBlockchainJobManager
    {
        Task StartAsync(PoolConfig poolConfig, StratumServer stratum);
        Task<bool> ValidateAddressAsync(string address);

        Task<object[]> HandleWorkerSubscribeAsync(StratumClient worker);
        Task<bool> HandleWorkerAuthenticateAsync(StratumClient worker, string workername, string password);
        Task<bool> HandleWorkerSubmitAsync(StratumClient worker, object submission);

        IObservable<object> Jobs { get; }
        NetworkStats NetworkStats { get; }
    }
}
