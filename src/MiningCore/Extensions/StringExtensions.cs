/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Globalization;

namespace MiningCore.Extensions
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
            if (str.StartsWith("0x"))
                str = str.Substring(2);

            var arr = new byte[str.Length >> 1];

            for(var i = 0; i < str.Length >> 1; ++i)
                arr[i] = (byte) ((GetHexVal(str[i << 1]) << 4) + GetHexVal(str[(i << 1) + 1]));

            return arr;
        }

        private static int GetHexVal(char hex)
        {
            var val = (int) hex;
            return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
        }

        public static string ToStringHex8(this uint value)
        {
            return value.ToString("x8", CultureInfo.InvariantCulture);
        }

        public static string ToStringHex8(this int value)
        {
            return value.ToString("x8", CultureInfo.InvariantCulture);
        }

        public static string ToStringHexWithPrefix(this ulong value)
        {
            if (value == 0)
                return "0x0";

            return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
        }

        public static string ToStringHexWithPrefix(this long value)
        {
            if (value == 0)
                return "0x0";

            return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
        }

        public static string ToStringHexWithPrefix(this uint value)
        {
            if (value == 0)
                return "0x0";

            return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
        }

        public static string ToStringHexWithPrefix(this int value)
        {
            if (value == 0)
                return "0x0";

            return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
        }

        public static T IntegralFromHex<T>(this string value)
        {
            var underlyingType = Nullable.GetUnderlyingType(typeof(T));

            if (value.StartsWith("0x"))
                value = value.Substring(2);

            if (!ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var val))
                throw new FormatException();

            return (T) Convert.ChangeType(val, underlyingType ?? typeof(T));
        }

        public static string ToLowerCamelCase(this string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            return char.ToLowerInvariant(str[0]) + str.Substring(1);
        }
    }
}
