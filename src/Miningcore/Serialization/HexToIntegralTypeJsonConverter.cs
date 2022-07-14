using System.Globalization;
using System.Numerics;
using Miningcore.Extensions;
using NBitcoin;
using Newtonsoft.Json;

namespace Miningcore.Serialization;

public class HexToIntegralTypeJsonConverter<T> : JsonConverter
{
    private readonly Type underlyingType = Nullable.GetUnderlyingType(typeof(T));

    public override bool CanConvert(Type objectType)
    {
        return typeof(T) == objectType;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if(value == null)
            writer.WriteValue("null");
        else
            writer.WriteValue($"0x{value:x}");
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var str = (string) reader.Value;

        if(string.IsNullOrEmpty(str))
            return default(T);

        if(str.StartsWith("0x"))
            str = str[2..];

        if(typeof(T) == typeof(BigInteger))
            return BigInteger.Parse("0" + str, NumberStyles.HexNumber);

        if(typeof(T) == typeof(uint256))
            return new uint256(str.HexToReverseByteArray());

        var val = ulong.Parse("0" + str, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Convert.ChangeType(val, underlyingType ?? typeof(T));
    }
}
