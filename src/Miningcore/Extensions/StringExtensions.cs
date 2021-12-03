using System.Buffers;
using System.Globalization;
using System.Text;

namespace Miningcore.Extensions;

public static class StringExtensions
{
    /// <summary>
    /// Converts a hex string to byte array
    /// </summary>
    /// <param name="str"></param>
    /// <returns></returns>
    public static byte[] HexToByteArray(this string str)
    {
        if(str.StartsWith("0x"))
            str = str[2..];

        var arr = new byte[str.Length >> 1];
        var count = str.Length >> 1;

        for(var i = 0; i < count; ++i)
            arr[i] = (byte) ((GetHexVal(str[i << 1]) << 4) + GetHexVal(str[(i << 1) + 1]));

        return arr;
    }

    /// <summary>
    /// Converts a hex string to byte array
    /// </summary>
    /// <param name="str"></param>
    /// <returns></returns>
    public static byte[] HexToReverseByteArray(this string str)
    {
        if(str.StartsWith("0x"))
            str = str[2..];

        var arr = new byte[str.Length >> 1];
        var count = str.Length >> 1;

        for(var i = 0; i < count; ++i)
            arr[count - 1 - i] = (byte) ((GetHexVal(str[i << 1]) << 4) + GetHexVal(str[(i << 1) + 1]));

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
        if(value == 0)
            return "0x0";

        return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
    }

    public static string ToStringHexWithPrefix(this long value)
    {
        if(value == 0)
            return "0x0";

        return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
    }

    public static string ToStringHexWithPrefix(this uint value)
    {
        if(value == 0)
            return "0x0";

        return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
    }

    public static string ToStringHexWithPrefix(this int value)
    {
        if(value == 0)
            return "0x0";

        return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
    }

    public static string StripHexPrefix(this string value)
    {
        if(value?.ToLower().StartsWith("0x") == true)
            return value[2..];

        return value;
    }

    public static T IntegralFromHex<T>(this string value)
    {
        var underlyingType = Nullable.GetUnderlyingType(typeof(T));

        if(value.StartsWith("0x"))
            value = value[2..];

        if(!ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var val))
            throw new FormatException();

        return (T) Convert.ChangeType(val, underlyingType ?? typeof(T));
    }

    public static string ToLowerCamelCase(this string str)
    {
        if(string.IsNullOrEmpty(str))
            return str;

        return char.ToLowerInvariant(str[0]) + str[1..];
    }

    public static string AsString(this ReadOnlySequence<byte> line, Encoding encoding)
    {
        return encoding.GetString(line.ToSpan());
    }

    public static string Capitalize(this string str)
    {
        if(string.IsNullOrEmpty(str))
            return str;

        return str[..1].ToUpper() + str[1..];
    }
}
