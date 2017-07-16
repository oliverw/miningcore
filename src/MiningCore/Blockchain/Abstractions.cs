using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MiningCore.Configuration;
using MiningCore.JsonRpc;
using Newtonsoft.Json.Linq;

namespace MiningCore.Blockchain
{
    public interface IBlockchainDemon
    {
        Task<bool> ValidateAddressAsync(string address);
        Task InitAsync(PoolConfig config);
        Task<bool> IsHealthyAsync();
    }
}
