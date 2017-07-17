using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace MiningForce.Extensions
{
    public static class StringExtensions
    {
        /// <summary>
        /// Converts a str string to byte array.
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static byte[] HexToByteArray(this string str)
        {
            byte[] arr = new byte[str.Length >> 1];

            for (int i = 0; i < str.Length >> 1; ++i)
            {
                arr[i] = (byte)((GetHexVal(str[i << 1]) << 4) + (GetHexVal(str[(i << 1) + 1])));
            }

            return arr;
        }

        private static int GetHexVal(char hex)
        {
            int val = (int)hex;
            return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
        }

        public static BigInteger BigIntFromBitsHex(this string bits)
        {
            return bits.HexToByteArray().BigIntFromBitsBuffer();
        }

        public static BigInteger BigIntFromBitsBuffer(this byte[] buffer)
        {
            var numBytes = Convert.ToByte(buffer.Take(1));
            var bigIntBits = new BigInteger(buffer.Skip(1).ToArray());  // buffer.Slice(1, buffer.Length - 1));

            var multiplier = new BigInteger(2 ^ 8 * (numBytes - 3));
            var target = BigInteger.Multiply(bigIntBits, multiplier);

            return target;
        }
    }
}
