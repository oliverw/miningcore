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
    public interface IBlockchainDemon
    {
        Task<bool> ValidateAddressAsync(string address);
        Task StartAsync(PoolConfig config);
        Task<bool> IsHealthyAsync();
    }

    public interface IMiningJobManager
    {
        Task StartAsync(PoolConfig poolConfig);
        void RegisterWorker(StratumClient worker);
        Task<object> GetStratumSubscribeParamsAsync();
    }
}
