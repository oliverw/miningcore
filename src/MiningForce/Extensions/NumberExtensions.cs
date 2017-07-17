using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace MiningForce.Extensions
{
    public static class NumberExtensions
    {
        public static UInt32 ToBigEndian(this UInt32 value)
        {
            if (BitConverter.IsLittleEndian)
                return (uint) IPAddress.NetworkToHostOrder((int) value);

            return value;
        }
    }
}
