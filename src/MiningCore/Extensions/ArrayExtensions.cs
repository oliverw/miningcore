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
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MiningCore.Extensions
{
    public static class ArrayExtensions
    {
        public static string ToHexString(this IEnumerable<byte> byteArray)
        {
            return ToHexString(byteArray.ToArray());
        }

        public static string ToHexString(this byte[] byteArray, bool withPrefix = false)
        {
            var result = byteArray.Aggregate("", (current, b) => current + b.ToString("x2"));

            if (withPrefix)
                result = "0x" + result;

            return result;
        }

        public static string ToFormatedHexString(this byte[] byteArray)
        {
            return byteArray.Aggregate("", (current, b) => current + " 0x" + b.ToString("x2"));
        }

        /// <summary>
        /// Apparently mixing big-ending and little-endian isn't confusing enough so sometimes every
        /// block of 4 bytes must be reversed before reversing the entire buffer
        /// </summary>
        public static byte[] ReverseByteOrder(this byte[] bytes)
        {
            using(var stream = new MemoryStream())
            {
                using(var writer = new BinaryWriter(stream))
                {
                    for(var i = 0; i < 8; i++)
                    {
                        var value = BitConverter.ToUInt32(bytes, i * 4).ToBigEndian();
                        writer.Write(value);
                    }

                    writer.Flush();
                    return stream.ToArray().Reverse().ToArray();
                }
            }
        }

        public static T[] ToReverseArray<T>(this IEnumerable<T> bytes)
        {
            var arr = bytes.ToArray();
            Array.Reverse(arr);
            return arr;
        }

        public static T[] ToReverseArray<T>(this T[] arr)
        {
            Array.Reverse(arr);
            return arr;
        }
    }
}
