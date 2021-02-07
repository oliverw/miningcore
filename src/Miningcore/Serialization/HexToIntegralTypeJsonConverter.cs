using System;
using System.Globalization;
using System.Numerics;
using Miningcore.Extensions;
using NBitcoin;
using Newtonsoft.Json;

namespace Miningcore.Serialization
{
    public class HexToIntegralTypeJsonConverter<T> : JsonConverter
    {
        private readonly Type underlyingType = Nullable.GetUnderlyingType(typeof(T));

        public override bool CanConvert(Type objectType)
        {
            Console.WriteLine($"CONVERT type: {objectType}");

            return typeof(T) == objectType;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            Console.WriteLine($"WRITE writer: {writer}");
            Console.WriteLine($"WRITE value: {value}");
            Console.WriteLine($"WRITE serializer: {serializer}");

            if(value == null)
                writer.WriteValue("null");
            else
                writer.WriteValue($"0x{value:x}");
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var str = (string) reader.Value;

            Console.WriteLine($"READ str: {str}");
            Console.WriteLine($"READ Type: {objectType}");
            Console.WriteLine($"READ serializer: {serializer}");

            if(string.IsNullOrEmpty(str))
                return default(T);

            if(str.StartsWith("0x"))
                str = str.Substring(2);

            if(typeof(T) == typeof(BigInteger))
                return BigInteger.Parse(str, NumberStyles.HexNumber);

            //if(typeof(T) == typeof(BigInteger))
            //    return BigInteger.Parse("0" + str, NumberStyles.HexNumber);

            if(typeof(T) == typeof(uint256))
                return new uint256(str.HexToReverseByteArray());

            var val = ulong.Parse(str, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            //var val = ulong.Parse("0" + str, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Convert.ChangeType(val, underlyingType ?? typeof(T));
        }
    }
}
