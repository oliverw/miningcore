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
    public class SubscribeResponse
    {
        public string SubscriptionId { get; set; }
        public string Extranonce1 { get; set; }
        public int Extranonce2Length { get; set; }
    }

    public interface IBlockchainJobManager
    {
        Task StartAsync(PoolConfig poolConfig, StratumServer stratum);
        Task<bool> ValidateAddressAsync(string address);

        Task<SubscribeResponse> HandleWorkerSubscribeAsync(StratumClient worker);
        Task<bool> HandleWorkerAuthenticateAsync(StratumClient worker, string workername, string password);
        Task<bool> HandleWorkerSubmitAsync(StratumClient worker, object submission);

        IObservable<object> Jobs { get; }
    }
}
