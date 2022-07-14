using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Banning;

public class IntegratedBanManager : IBanManager
{
    private static readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions
    {
        ExpirationScanFrequency = TimeSpan.FromSeconds(10)
    });

    #region Implementation of IBanManager

    public bool IsBanned(IPAddress address)
    {
        var result = cache.Get(address.ToString());
        return result != null;
    }

    public void Ban(IPAddress address, TimeSpan duration)
    {
        Contract.RequiresNonNull(address);
        Contract.Requires<ArgumentException>(duration.TotalMilliseconds > 0);

        // don't ban loopback
        if(address.Equals(IPAddress.Loopback) || address.Equals(IPAddress.IPv6Loopback))
            return;

        cache.Set(address.ToString(), string.Empty, duration);
    }

    #endregion
}
