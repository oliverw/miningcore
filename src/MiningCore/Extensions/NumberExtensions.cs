using System;
using System.Net;

namespace MiningCore.Extensions
{
    public static class NumberExtensions
    {
        public static uint ToBigEndian(this uint value)
        {
            if (BitConverter.IsLittleEndian)
                return (uint) IPAddress.NetworkToHostOrder((int) value);

            return value;
        }

        public static uint ToLittleEndian(this uint value)
        {
            if (!BitConverter.IsLittleEndian)
                return (uint) IPAddress.HostToNetworkOrder((int) value);

            return value;
        }
    }
}
