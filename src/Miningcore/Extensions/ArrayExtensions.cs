using System.Numerics;
using System.Text;

namespace Miningcore.Extensions;

public static class ArrayExtensions
{
    private static readonly string[] HexStringTable =
    {
        "00", "01", "02", "03", "04", "05", "06", "07", "08", "09", "0a", "0b", "0c", "0d", "0e", "0f",
        "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "1a", "1b", "1c", "1d", "1e", "1f",
        "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "2a", "2b", "2c", "2d", "2e", "2f",
        "30", "31", "32", "33", "34", "35", "36", "37", "38", "39", "3a", "3b", "3c", "3d", "3e", "3f",
        "40", "41", "42", "43", "44", "45", "46", "47", "48", "49", "4a", "4b", "4c", "4d", "4e", "4f",
        "50", "51", "52", "53", "54", "55", "56", "57", "58", "59", "5a", "5b", "5c", "5d", "5e", "5f",
        "60", "61", "62", "63", "64", "65", "66", "67", "68", "69", "6a", "6b", "6c", "6d", "6e", "6f",
        "70", "71", "72", "73", "74", "75", "76", "77", "78", "79", "7a", "7b", "7c", "7d", "7e", "7f",
        "80", "81", "82", "83", "84", "85", "86", "87", "88", "89", "8a", "8b", "8c", "8d", "8e", "8f",
        "90", "91", "92", "93", "94", "95", "96", "97", "98", "99", "9a", "9b", "9c", "9d", "9e", "9f",
        "a0", "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9", "aa", "ab", "ac", "ad", "ae", "af",
        "b0", "b1", "b2", "b3", "b4", "b5", "b6", "b7", "b8", "b9", "ba", "bb", "bc", "bd", "be", "bf",
        "c0", "c1", "c2", "c3", "c4", "c5", "c6", "c7", "c8", "c9", "ca", "cb", "cc", "cd", "ce", "cf",
        "d0", "d1", "d2", "d3", "d4", "d5", "d6", "d7", "d8", "d9", "da", "db", "dc", "dd", "de", "df",
        "e0", "e1", "e2", "e3", "e4", "e5", "e6", "e7", "e8", "e9", "ea", "eb", "ec", "ed", "ee", "ef",
        "f0", "f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "fa", "fb", "fc", "fd", "fe", "ff"
    };

    public static BigInteger ToBigInteger(this Span<byte> value)
    {
        return new(value, true);
    }

    public static string ToHexString(this IEnumerable<byte> byteArray)
    {
        return ToHexString(byteArray.ToArray());
    }

    public static string ToHexString(this byte[] value, bool withPrefix = false)
    {
        return ToHexString(value, null, null, withPrefix);
    }

    public static string ToHexString(this Span<byte> value, bool withPrefix = false)
    {
        return ToHexString(value, null, null, withPrefix);
    }

    public static string ToByteArrayBase64(this string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    public static string ToHexString(this Span<byte> value, int? off, int? len, bool withPrefix = false)
    {
        if(value == null || value.Length == 0)
            return string.Empty;

        var length = len ?? value.Length;
        var bufferSize = length * 2;

        if(withPrefix)
            bufferSize += 2;

        Span<char> buffer = stackalloc char[bufferSize];

        var offset = 0;

        if(withPrefix)
        {
            buffer[offset++] = '0';
            buffer[offset++] = 'x';
        }

        var start = off ?? 0;

        for(var i = start; i < length; i++)
        {
            var hex = HexStringTable[value[i]];
            buffer[offset + i * 2 + 0] = hex[0];
            buffer[offset + i * 2 + 1] = hex[1];
        }

        return new string(buffer);
    }


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
                return stream.ToArray().ReverseInPlace();
            }
        }
    }

    public static T[] ToNewReverseArray<T>(this IEnumerable<T> bytes)
    {
        var arr = bytes.ToArray();
        Array.Reverse(arr);
        return arr;
    }

    public static Span<T> ToNewReverseArray<T>(this Span<T> bytes)
    {
        var arr = bytes.ToArray();
        Array.Reverse(arr);
        return arr;
    }

    public static T[] ReverseInPlace<T>(this T[] arr)
    {
        Array.Reverse(arr);
        return arr;
    }

    public static byte[] PadFront(this byte[] bytes, byte padValue, int desiredLength)
    {
        if(bytes.Length >= desiredLength)
            return bytes;

        var result = Enumerable.Repeat(padValue, desiredLength - bytes.Length)
            .Concat(bytes)
            .ToArray();

        return result;
    }
}
