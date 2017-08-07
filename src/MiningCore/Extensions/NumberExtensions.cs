using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace MiningCore.Extensions
{
    public static class NumberExtensions
    {
        public static UInt32 ToBigEndian(this UInt32 value)
        {
            if (BitConverter.IsLittleEndian)
                return (uint) IPAddress.NetworkToHostOrder((int) value);

            return value;
        }

	    public static UInt32 ToLittleEndian(this UInt32 value)
	    {
		    if (!BitConverter.IsLittleEndian)
				return (uint)IPAddress.HostToNetworkOrder((int)value);

			return value;
	    }
	}
}
