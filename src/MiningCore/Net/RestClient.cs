using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeContracts;
using Newtonsoft.Json;
using HttpUtility = System.Net.WebUtility;

namespace MiningCore.Net
{
    public class RestClient
    {
        public RestClient(string endpoint, HttpClient client)
        {
            this.endpoint = endpoint;
            this.client = client;
        }

        protected readonly string endpoint;
        private readonly HttpClient client;

        public string UserAgent { get; set; }

        private static void ThrowIfNotSuccessStatusCode(HttpResponseMessage httpResponse, string content)
        {
            if (!httpResponse.IsSuccessStatusCode)
                throw new RestRequestException(httpResponse.StatusCode, content);
        }

        protected static string UrlEncode(string value)
        {
            return HttpUtility.UrlEncode(value);
        }

        public async Task<RestResponse<string>> ExecuteGetStringAsync(RestRequest request, CancellationToken? ct = null)
        {
            Contract.RequiresNonNull(request, nameof(request));

            using (var httpResponse = await ExecuteInternal(request, ct))
            {
                var content = await httpResponse.Content.ReadAsStringAsync();

                return new RestResponse<string>(httpResponse.StatusCode, httpResponse.IsSuccessStatusCode, content,
                    content, httpResponse.Headers);
            }
        }

        public async Task<RestResponse<byte[]>> ExecuteGetBytesAsync(RestRequest request, CancellationToken? ct = null)
        {
            Contract.RequiresNonNull(request, nameof(request));

            using (var httpResponse = await ExecuteInternal(request, ct))
            {
                var content = await httpResponse.Content.ReadAsByteArrayAsync();

                return new RestResponse<byte[]>(httpResponse.StatusCode, httpResponse.IsSuccessStatusCode, content,
                    Encoding.UTF8.GetString(content), httpResponse.Headers);
            }
        }

        public async Task<string> GetStringAsync(RestRequest request, CancellationToken? ct = null)
        {
            Contract.RequiresNonNull(request, nameof(request));

            using (var httpResponse = await ExecuteInternal(request, ct))
            {
                var content = await httpResponse.Content.ReadAsStringAsync();

                ThrowIfNotSuccessStatusCode(httpResponse, content);

                return content;
            }
        }

        public async Task<byte[]> GetBytesAsync(RestRequest request, CancellationToken? ct = null)
        {
            Contract.RequiresNonNull(request, nameof(request));

            using (var httpResponse = await ExecuteInternal(request, ct))
            {
                var content = await httpResponse.Content.ReadAsByteArrayAsync();

                ThrowIfNotSuccessStatusCode(httpResponse, Encoding.UTF8.GetString(content));

                return content;
            }
        }

        public async Task<RestResponse<T>> ExecuteAsync<T>(RestRequest request, CancellationToken? ct = null)
        {
            Contract.RequiresNonNull(request, nameof(request));

            using (var httpResponse = await ExecuteInternal(request, ct))
            {
                ThrowIfNotSuccessStatusCode(httpResponse, httpResponse.Content.ToString());

                // attempt to deserialize using json.net
                var json = await httpResponse.Content.ReadAsStringAsync();

                using (var reader = new StringReader(json))
                {
                    using (var jsonReader = new JsonTextReader(reader))
                    {
                        var serializer = new JsonSerializer();
                        return new RestResponse<T>(httpResponse.StatusCode, httpResponse.IsSuccessStatusCode,
                            serializer.Deserialize<T>(jsonReader), json, httpResponse.Headers);
                    }
                }
            }
        }

        public async Task<RestResponse<TReponse>> ExecuteAsync<TReponse, T>(RestRequest<T> request, CancellationToken? ct = null)
        {
            Contract.RequiresNonNull(request, nameof(request));

            using (var httpResponse = await ExecuteInternal(request, ct))
            {
                ThrowIfNotSuccessStatusCode(httpResponse, httpResponse.Content.ToString());

                // attempt to deserialize using json.net
                var json = await httpResponse.Content.ReadAsStringAsync();

                using (var reader = new StringReader(json))
                {
                    using (var jsonReader = new JsonTextReader(reader))
                    {
                        var serializer = new JsonSerializer();
                        return new RestResponse<TReponse>(httpResponse.StatusCode, httpResponse.IsSuccessStatusCode,
                            serializer.Deserialize<TReponse>(jsonReader), json, httpResponse.Headers);
                    }
                }
            }
        }

        protected virtual async Task<HttpResponseMessage> ExecuteInternal(RestRequest request, CancellationToken? ct)
        {
            Contract.RequiresNonNull(request, nameof(request));

            var requestUrlBuilder = new StringBuilder(endpoint);

            if (!endpoint.EndsWith("/") && !request.resource.StartsWith("/"))
                requestUrlBuilder.Append("/");

            if (!string.IsNullOrEmpty(request.resource))
                requestUrlBuilder.Append(request.resource);

            if (request.parameters != null && request.parameters.Count > 0)
            {
                requestUrlBuilder.Append("?");
                requestUrlBuilder.Append(string.Join("&", request.parameters.Select(x => $"{x.Key}={UrlEncode(x.Value)}")));
            }

            if (!string.IsNullOrEmpty(UserAgent))
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

            using (var msg = new HttpRequestMessage
            {
                RequestUri = new Uri(requestUrlBuilder.ToString()),
                Method = request.method,
                Content = request.Content,
            })
            {
                if (request.headers != null && request.headers.Count > 0)
                {
                    foreach (var header in request.headers.Keys)
                        msg.Headers.Add(header, request.headers[header]);
                }

                if (ct.HasValue)
                    return await client.SendAsync(msg, ct.Value);
                else
                    return await client.SendAsync(msg);
            }
        }
    }
}
