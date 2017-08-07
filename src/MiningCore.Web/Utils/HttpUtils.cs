using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace MiningCore.Utils
{
    public static class HttpUtils
    {
        public static string GetClientAddress(this HttpContext context)
        {
            return context.Connection.RemoteIpAddress.ToString();
        }

        public static string SetQueryParameter(string url, string parameter, object newValue = null)
        {
            url = QueryHelpers.AddQueryString(url, parameter, newValue?.ToString() ?? string.Empty);

            // strip trailing equal sign if value-less query
            if (url.EndsWith("="))
                url = url.Substring(0, url.Length - 1);

            return url;
        }

        public static RouteValueDictionary SetValue(this RouteValueDictionary dictionary, string key, object value)
        {
            var rvd = new RouteValueDictionary(dictionary)
            {
                [key] = value
            };

            return rvd;
        }

        public static RouteData SetValue(this RouteData dictionary, string key, object value)
        {
            var rvd = new RouteData(dictionary);
            rvd.Values[key] = value;

            return rvd;
        }

        public static IDictionary<string, string> ToStringDictionary(this RouteValueDictionary dictionary)
        {
            var result = new Dictionary<string, string>();

            foreach (var key in dictionary.Keys)
                result[key] = (string) dictionary[key];

            return result;
        }

        public static readonly Regex regexStripNewLines = new Regex("(\r\n|\r|\n)", RegexOptions.Compiled);

        public static string Newlines2Spaces(string str)
        {
            return regexStripNewLines.Replace(str, " ");
        }
    }
}