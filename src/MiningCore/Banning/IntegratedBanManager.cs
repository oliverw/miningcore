using System;
using System.Net;
using CodeContracts;
using Microsoft.Extensions.Caching.Memory;

namespace MiningCore.Banning
{
    public class IntegratedBanManager : IBanManager
    {
        private static readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions
        {
            ExpirationScanFrequency = TimeSpan.FromSeconds(10)
        });

        #region Implementation of IBanManager

        public bool IsBanned(IPAddress address)
        {
            Contract.RequiresNonNull(address, nameof(address));

            var result = cache.Get(address.ToString());
            return result != null;
        }

        public void Ban(IPAddress address, TimeSpan duration)
        {
            Contract.RequiresNonNull(address, nameof(address));
            Contract.Requires<ArgumentException>(duration.TotalMilliseconds > 0,
                $"{nameof(duration)} must not be empty");

            cache.Set(address.ToString(), string.Empty, duration);
        }

        #endregion
    }
}