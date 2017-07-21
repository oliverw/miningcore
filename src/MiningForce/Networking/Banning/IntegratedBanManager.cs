using System;
using System.Net;
using Microsoft.Extensions.Caching.Memory;

namespace MiningForce.Networking.Banning
{
    public class IntegratedBanManager : IBanManager
    {
	    private static readonly IMemoryCache bannedIpCache = new MemoryCache(new MemoryCacheOptions
	    {
		    ExpirationScanFrequency = TimeSpan.FromSeconds(10)
	    });

	    #region Implementation of IBanManager

		public bool IsBanned(IPAddress address)
		{
			var result = bannedIpCache.Get(address.ToString());
			return result != null;
		}

	    public void Ban(IPAddress address, TimeSpan duration)
	    {
		    bannedIpCache.Set(address.ToString(), string.Empty, duration);
	    }

		#endregion
	}
}
