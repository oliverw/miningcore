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

        public static string ToHexString(this byte[] byteArray)
        {
            return byteArray.Aggregate("", (current, b) => current + b.ToString("x2"));
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
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    for (var i = 0; i < 8; i++)
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
