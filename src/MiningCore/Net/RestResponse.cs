using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;

namespace MiningCore.Net
{
    public class RestResponse<T>
    {
        internal RestResponse(HttpStatusCode statusCode, bool success, T content, string stringContent, HttpResponseHeaders headers)
        {
            this.Content = content;
            this.StringContent = stringContent;
            this.StatusCode = statusCode;
            this.Success = success;

            this.headers = headers.ToDictionary(x => x.Key, x => x.Value.ToArray());
        }

        internal Dictionary<string, string[]> headers;

        public HttpStatusCode StatusCode { get; }
        public bool Success { get; }
        public T Content { get; }
        public string StringContent { get; }
        public Dictionary<string, string[]> Headers => headers;
    }
}
