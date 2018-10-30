using System;
using System.Collections.Generic;
using System.Text;

namespace Miningcore.Api
{
    class ApiException : Exception
    {
        public ApiException(string message, int? responseStatusCode = null) : base(message)
        {
            ResponseStatusCode = responseStatusCode;
        }

        public ApiException()
        {
        }

        public int? ResponseStatusCode { get; }
    }
}
