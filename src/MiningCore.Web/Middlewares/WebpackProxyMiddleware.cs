using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace MiningCore.Middlewares
{
    public class WebpackProxyMiddlewareOptions
    {
        public string[] IncludePaths { get; set; }
        public HttpMessageHandler BackChannelMessageHandler { get; set; }
    }

    public class WebpackProxyMiddleware
    {
        private readonly RequestDelegate next;
        private readonly HttpClient httpClient;
        private readonly WebpackProxyMiddlewareOptions options;

        public WebpackProxyMiddleware(RequestDelegate next, IOptions<WebpackProxyMiddlewareOptions> options)
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            this.next = next;
            this.options = options.Value;

            if (this.options.IncludePaths == null || this.options.IncludePaths.Length == 0)
                throw new ArgumentException("Options parameter must specify paths to include.", nameof(options));

            httpClient = new HttpClient(this.options.BackChannelMessageHandler ?? new HttpClientHandler());
        }

        public async Task Invoke(HttpContext context)
        {
            var requestMessage = new HttpRequestMessage();

            // check if request applies to us
            if (!options.IncludePaths.Any(path => context.Request.Path.Value.StartsWith(path)))
            {
                await next(context);
                return;
            }

            if (!string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(context.Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(context.Request.Method, "DELETE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(context.Request.Method, "TRACE", StringComparison.OrdinalIgnoreCase))
            {
                var streamContent = new StreamContent(context.Request.Body);
                requestMessage.Content = streamContent;
            }

            // Copy the request headers
            foreach (var header in context.Request.Headers)
            {
                if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                    requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            requestMessage.Headers.Host = AppConstants.WebPackDevServerBaseUri.Host + ":" + AppConstants.WebPackDevServerBaseUri.Port;
            var uriString = $"{AppConstants.WebPackDevServerBaseUri.Scheme}://{AppConstants.WebPackDevServerBaseUri.Host}:{AppConstants.WebPackDevServerBaseUri.Port}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            requestMessage.RequestUri = new Uri(uriString);
            requestMessage.Method = new HttpMethod(context.Request.Method);

            using (var responseMessage = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted))
            {
                context.Response.StatusCode = (int)responseMessage.StatusCode;
                foreach (var header in responseMessage.Headers)
                    context.Response.Headers[header.Key] = header.Value.ToArray();

                foreach (var header in responseMessage.Content.Headers)
                    context.Response.Headers[header.Key] = header.Value.ToArray();

                // SendAsync removes chunking from the response. This removes the header so it doesn't expect a chunked response.
                context.Response.Headers.Remove("transfer-encoding");
                await responseMessage.Content.CopyToAsync(context.Response.Body);
            }
        }
    }
}
