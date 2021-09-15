using System;
using System.Buffers;
using System.Text.Json;

namespace Miningcore.Extensions
{
    public static class JsonExtensions
    {
        public static T ToObject<T>(this JsonElement element, JsonSerializerOptions options = null)
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(bufferWriter))
                element.WriteTo(writer);
            return JsonSerializer.Deserialize<T>(bufferWriter.WrittenSpan, options);
        }

        public static T ToObject<T>(this JsonDocument document, JsonSerializerOptions options = null)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            return document.RootElement.ToObject<T>(options);
        }

        public static JsonDocument JsonDocumentFromObject<TValue>(TValue value, JsonSerializerOptions options = default)
            => JsonDocumentFromObject(value, typeof(TValue), options);

        public static JsonDocument JsonDocumentFromObject(object value, Type type, JsonSerializerOptions options = default)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
            return JsonDocument.Parse(bytes);
        }

        public static JsonElement JsonElementFromObject<TValue>(TValue value, JsonSerializerOptions options = default)
            => JsonElementFromObject(value, typeof(TValue), options);

        public static JsonElement JsonElementFromObject(object value, Type type, JsonSerializerOptions options = default)
        {
            using var doc = JsonDocumentFromObject(value, type, options);
            return doc.RootElement.Clone();
        }
    }
}
