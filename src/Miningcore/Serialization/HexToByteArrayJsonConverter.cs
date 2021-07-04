using System;
using System.Globalization;
using Miningcore.Extensions;
using Newtonsoft.Json;

namespace Miningcore.Serialization
{
    public class HexToByteArrayJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(byte[]) == objectType;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if(value == null)
                writer.WriteValue("null");
            else
            {
                // Remove all 0 at the beginning. 0 after 0x is not allowed when the value is not 0
                object valueToHex = $"{value:x}".TrimStart(new Char[] { '0' });
                // If value was 0, after trim it is null. Correcting it to 0x0.
                if(object.Equals(valueToHex, ""))
                {
                    writer.WriteValue($"0x{value:x}");
                }
                else
                {
                    writer.WriteValue($"0x{valueToHex}");
                }
            }

        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var str = (string) reader.Value;
            if(str.StartsWith("0x"))
                str = str[2..];

            if(string.IsNullOrEmpty(str))
                return null;

            return str.HexToByteArray();
        }
    }
}
