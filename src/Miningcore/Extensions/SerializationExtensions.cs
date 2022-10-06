using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Miningcore.Extensions;

public static class SerializationExtensions
{
    private static readonly JsonSerializer serializer = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy()
        }
    };

    public static T SafeExtensionDataAs<T>(this IDictionary<string, object> extra, params string[] wrappers)
    {
        if(extra == null)
            return default;

        try
        {
            object o = extra;

            foreach (var key in wrappers)
            {
                if(o is IDictionary<string, object> dict)
                    o = dict[key];

                else if(o is JObject jo)
                    o = jo[key];

                else
                    throw new NotSupportedException("Unsupported child element type");
            }

            return JToken.FromObject(o).ToObject<T>(serializer);
        }

        catch
        {
            // ignored
        }

        return default;
    }
}
