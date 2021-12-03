using System.Net;

namespace Miningcore.Extensions;

public static class IpAddressExtensions
{
    public static bool IsEqual(this IPAddress address, IPAddress other)
    {
        if(address.Equals(other))
            return true;

        if(address.IsIPv4MappedToIPv6 && !other.IsIPv4MappedToIPv6 && address.MapToIPv4().Equals(other))
            return true;

        if(address.IsIPv4MappedToIPv6 && other.IsIPv4MappedToIPv6 && address.MapToIPv4().Equals(other.MapToIPv4()))
            return true;

        if(!address.IsIPv4MappedToIPv6 && other.IsIPv4MappedToIPv6 && address.Equals(other.MapToIPv4()))
            return true;

        return false;
    }
}
