using System;
using System.Linq;

namespace MiningCore.Extensions
{
    public static class StringExtensions
    {
        public static string ToBase64(this long value)
        {
            var bits = BitConverter.GetBytes(value);
            var result = Convert.ToBase64String(bits);
            return result;
        }

        public static string ToBase64(this int value)
        {
            var bits = BitConverter.GetBytes(value);
            var result = Convert.ToBase64String(bits);
            return result;
        }

        public static long ToInt64(this string value)
        {
            var array = Convert.FromBase64String(value);
            var result = BitConverter.ToInt64(array, 0);
            return result;
        }

        public static string Hex2Base64(this string value)
        {
            return Convert.ToBase64String(Enumerable.Range(0, value.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(value.Substring(x, 2), 16))
                .ToArray());
        }
    }
}
