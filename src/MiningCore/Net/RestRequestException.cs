using System;
using System.Net;

namespace MiningCore.Net
{
    public class RestRequestException : Exception
    {
        public RestRequestException(HttpStatusCode code, string content) :
            base($"Request failed with status code {(int)code} ({code.ToString().ToLower()}): {content}")
        {
            StatusCode = code;
        }

        public HttpStatusCode StatusCode { get; private set; }
    }
}
