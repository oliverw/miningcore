using System.Net;

namespace Miningcore.Extensions;

public static class NumberExtensions
{
    public static uint ToBigEndian(this uint value)
    {
        if(BitConverter.IsLittleEndian)
            return (uint) IPAddress.NetworkToHostOrder((int) value);

        return value;
    }

    public static uint ToLittleEndian(this uint value)
    {
        if(!BitConverter.IsLittleEndian)
            return (uint) IPAddress.HostToNetworkOrder((int) value);

        return value;
    }

    public static uint ReverseByteOrder(this uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        value = BitConverter.ToUInt32(bytes, 0);
        return value;
    }
}
