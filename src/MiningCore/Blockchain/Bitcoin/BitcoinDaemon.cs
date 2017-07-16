using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CodeContracts;
using MiningCore.Blockchain.Bitcoin.Messages;
using MiningCore.Configuration;
using Newtonsoft.Json;

namespace MiningCore.Blockchain.Bitcoin
{
    public class BitcoinDaemon : DemonBase,
        IBitcoinDaemon
    {
        public BitcoinDaemon(HttpClient httpClient, JsonSerializerSettings serializerSettings) : 
            base(httpClient, serializerSettings)
        {
            testInstanceOnlineCommand = "getinfo";
        }

        #region API-Surface

        public Task StartAsync(PoolConfig poolConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.Requires<ArgumentException>(poolConfig.Daemons.Length > 0, $"{nameof(poolConfig.Daemons)} must not be empty");

            this.endPoints = poolConfig.Daemons;

            return Task.FromResult(true);
        }

        public async Task<bool> ValidateAddressAsync(string address)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

            var result = await ExecuteCmdAnyAsync<string[], ValidateAddressResponse>("validateaddress", new [] {address});

            return result.Response != null && result.Response.IsValid;
        }

        #endregion // API-Surface
    }
}
