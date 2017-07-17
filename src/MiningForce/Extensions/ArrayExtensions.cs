using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MiningForce.Extensions
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

        public static byte[] DoubleDigest(this byte[] input)
        {
            using (var hash = SHA256.Create())
            {
                var first = hash.ComputeHash(input, 0, input.Length);
                return hash.ComputeHash(first);
            }
        }

        public static IEnumerable<byte> DoubleDigest(this IEnumerable<byte> input)
        {
            return input.ToArray().DoubleDigest();
        }
    }
}
