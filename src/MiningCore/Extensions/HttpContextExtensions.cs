using System;
using Microsoft.AspNetCore.Http;

namespace MiningCore.Extensions
{
    public static class HttpContextExtensions
    {
        public static T GetQueryParameter<T>(this HttpContext ctx, string name)
        {
            var stringVal = ctx.Request.Query[name];
            if (string.IsNullOrEmpty(stringVal))
                return default(T);

            return (T) Convert.ChangeType(stringVal, typeof(T));
        }
    }
}
