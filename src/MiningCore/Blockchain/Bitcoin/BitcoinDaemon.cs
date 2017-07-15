using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CodeContracts;
using MiningCore.Configuration;
using MiningCore.JsonRpc;
using MiningCore.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiningCore.Blockchain.Bitcoin
{
    public class BitcoinDaemon : DemonBase,
        IBitcoinDaemon
    {
        public BitcoinDaemon(HttpClient httpClient, JsonSerializerSettings serializerSettings) : 
            base(httpClient, serializerSettings)
        {
        }

        #region API-Surface

        public async Task InitAsync(PoolConfig poolConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.Requires<ArgumentException>(poolConfig.Daemons.Length > 0, $"{nameof(poolConfig.Daemons)} must not be empty");

            this.endPoints = poolConfig.Daemons;

            await EnsureOnline();
        }

        public Task<bool> ValidateAddressAsync(string address)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

            return Task.FromResult(true);
        }

        #endregion // API-Surface

        private async Task EnsureOnline()
        {
            try
            {
                var bla = await ExecuteCmdAllAsync("getinfo");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }
    }
}
