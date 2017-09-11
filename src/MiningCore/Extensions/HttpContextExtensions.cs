using System;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace MiningCore.Extensions
{
    public static class HttpContextExtensions
    {
        public static T GetQueryParameter<T>(this HttpContext ctx, string name, T defaultValue)
        {
            var stringVal = ctx.Request.Query[name].FirstOrDefault();
            if (string.IsNullOrEmpty(stringVal))
                return defaultValue;

            return (T) Convert.ChangeType(stringVal, typeof(T));
        }
    }
}
