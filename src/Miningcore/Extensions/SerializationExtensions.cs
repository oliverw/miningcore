using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Miningcore.Extensions
{
    public static class SerializationExtensions
    {
        private static readonly JsonSerializer serializer = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        public static T SafeExtensionDataAs<T>(this IDictionary<string, object> extra, string outerWrapper = null)
        {
            if(extra != null)
            {
                try
                {
                    var o = !string.IsNullOrEmpty(outerWrapper) ? extra[outerWrapper] : extra;

                    return JToken.FromObject(o).ToObject<T>(serializer);
                }

                catch(Exception)
                {
                    // ignored
                }
            }

            return default;
        }
    }
}
