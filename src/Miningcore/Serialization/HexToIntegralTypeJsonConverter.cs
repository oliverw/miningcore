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

            if(value == null)
                writer.WriteValue("null");
            else
            {

                object valueToHex = $"{value:x}".TrimStart(new Char[] { '0' });
                Console.WriteLine($"WRITE ToHex1: 0x{valueToHex}");
                if(valueToHex == null) {valueToHex = "0" }
                Console.WriteLine($"WRITE ToHex2: 0x{valueToHex}");
                writer.WriteValue($"0x{valueToHex}");
            }
                
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var str = (string) reader.Value;

            if(string.IsNullOrEmpty(str))
                return default(T);

            if(str.StartsWith("0x"))
                str = str.Substring(2);

            if(typeof(T) == typeof(BigInteger))
                return BigInteger.Parse("0" + str, NumberStyles.HexNumber);

            if(typeof(T) == typeof(uint256))
                return new uint256(str.HexToReverseByteArray());

            var val = ulong.Parse("0" + str, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Convert.ChangeType(val, underlyingType ?? typeof(T));
        }
    }
}
