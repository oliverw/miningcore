using System.Net;

namespace Miningcore.Api;

class ApiException : Exception
{
    public ApiException(string message, HttpStatusCode? responseStatusCode = null) : base(message)
    {
        if(responseStatusCode.HasValue)
            ResponseStatusCode = (int) responseStatusCode.Value;
    }

    public ApiException(HttpStatusCode responseStatusCode) : base(string.Empty)
    {
        ResponseStatusCode = (int) responseStatusCode;
    }

    public ApiException()
    {
    }

    public int? ResponseStatusCode { get; }
}
