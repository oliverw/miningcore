using System.Net;
using Miningcore.Contracts;

namespace Miningcore.Extensions;

public static class IpAddressExtensions
{
    public static bool IsEqual(this IPAddress address, IPAddress other)
    {
        Contract.RequiresNonNull(address);
        Contract.RequiresNonNull(other);

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

    public static IPAddress CensorOrReturn(this IPAddress address, bool censor)
    {
        Contract.RequiresNonNull(address);

        if(!censor)
            return address;

        if(address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var ipBytes = address.GetAddressBytes();

        if(ipBytes.Length == 4)
        {
            // IPv4
            // keep the first and last part
            ipBytes[2] = 0;
            ipBytes[3] = 0;
        }

        else if(ipBytes.Length == 16)
        {
            // IPv6
            // keep the first 2 and last 2 parts
            for(var i = 4; i < 12; i++)
                ipBytes[i] = 0;
        }

        return new IPAddress(ipBytes);
    }
}
