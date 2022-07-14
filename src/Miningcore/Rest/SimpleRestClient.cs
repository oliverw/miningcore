using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Miningcore.Contracts;

namespace Miningcore.Rest;

public class SimpleRestClient
{
    public SimpleRestClient(IHttpClientFactory factory, string baseUrl)
    {
        this.baseUrl = baseUrl;

        httpClient = factory.CreateClient();
    }

    protected virtual IEnumerable<KeyValuePair<string, string>> PrepareQueryParams(
        IEnumerable<KeyValuePair<string, string>> queryParams)
    {
        return queryParams;
    }

    protected virtual void PrepareRequest(HttpRequestMessage request,
        IEnumerable<KeyValuePair<string, string>> headers)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (headers != null)
        {
            foreach (var header in headers)
                request.Headers.Add(header.Key, header.Value);
        }
    }

    protected readonly HttpClient httpClient;
    protected readonly string baseUrl;

    protected readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public class ResponseContent<T> : IDisposable
    {
        public ResponseContent(HttpResponseMessage response, T content)
        {
            Response = response;
            Content = content;
        }

        public HttpResponseMessage Response { get; }
        public T Content { get; }

        public void Dispose()
        {
            Response?.Dispose();
        }
    }

    protected string BuildRequestUri(string path, IEnumerable<KeyValuePair<string, string>> queryParams = null)
    {
        var sb = new StringBuilder(baseUrl);

        if (!baseUrl.EndsWith('/') && !path.StartsWith('/'))
            sb.Append('/');

        sb.Append(path);

        var qp = queryParams?.ToArray();

        if (qp?.Length > 0)
        {
            sb.Append('?');

            for(var i=0;i<qp.Length;i++)
            {
                var pair = qp[i];
                var isLast = i == qp.Length - 1;

                sb.Append(pair.Key);
                sb.Append('=');
                sb.Append(HttpUtility.UrlEncode(pair.Value));

                if(!isLast)
                    sb.Append('&');
            }
        }

        return sb.ToString();
    }

    protected virtual string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, jsonSerializerOptions);
    }

    protected virtual T Deserialize<T>(string value)
    {
        return JsonSerializer.Deserialize<T>(value, jsonSerializerOptions);
    }

    private StringContent GetJsonContent(object data)
    {
        var json = Serialize(data);

        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    public async Task<T> Send<T>(HttpRequestMessage request, CancellationToken ct)
    {
        Contract.RequiresNonNull(request);

        PrepareRequest(request, null);

        using var response = await httpClient.SendAsync(request, ct);
        var msg = await response.Content.ReadAsStringAsync(ct);

        if(!response.IsSuccessStatusCode)
            throw new HttpRequestException(msg, null, response.StatusCode);

        return Deserialize<T>(msg);
    }

    public async Task<ResponseContent<T>> SendWithResponse<T>(HttpRequestMessage request, CancellationToken ct)
    {
        Contract.RequiresNonNull(request);

        PrepareRequest(request, null);

        using var response = await httpClient.SendAsync(request, ct);
        var msg = await response.Content.ReadAsStringAsync(ct);

        if(!response.IsSuccessStatusCode)
            throw new HttpRequestException(msg, null, response.StatusCode);

        return new ResponseContent<T>(response, Deserialize<T>(msg));
    }

    public async Task<T> Get<T>(string path, CancellationToken ct,
        IEnumerable<KeyValuePair<string, string>> queryParams = null,
        IEnumerable<KeyValuePair<string, string>> headers = null)
    {
        var requestUri = BuildRequestUri(path, PrepareQueryParams(queryParams));
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        PrepareRequest(request, headers);

        using var response = await httpClient.SendAsync(request, ct);
        var msg = await response.Content.ReadAsStringAsync(ct);

        if(!response.IsSuccessStatusCode)
            throw new HttpRequestException(msg, null, response.StatusCode);

        return Deserialize<T>(msg);
    }

    public async Task<HttpResponseMessage> PostWithResponse(string path, object data, CancellationToken ct,
        IEnumerable<KeyValuePair<string, string>> queryParams = null,
        IEnumerable<KeyValuePair<string, string>> headers = null)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(path));

        var requestUri = BuildRequestUri(path, PrepareQueryParams(queryParams));

        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = GetJsonContent(data)
        };

        PrepareRequest(request, headers);

        return await httpClient.SendAsync(request, ct);
    }

    public async Task<T> Post<T>(string path, object data, CancellationToken ct,
        IEnumerable<KeyValuePair<string, string>> queryParams = null,
        IEnumerable<KeyValuePair<string, string>> headers = null) where T: class
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(path));

        var requestUri = BuildRequestUri(path, PrepareQueryParams(queryParams));

        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = GetJsonContent(data)
        };

        PrepareRequest(request, headers);

        using var response = await httpClient.SendAsync(request, ct);
        var msg = await response.Content.ReadAsStringAsync(ct);

        if(!response.IsSuccessStatusCode)
            throw new HttpRequestException(msg, null, response.StatusCode);

        return Deserialize<T>(msg);
    }

    public async Task<ResponseContent<T>> PostWithResponse<T>(string path, object data, CancellationToken ct,
        IEnumerable<KeyValuePair<string, string>> queryParams = null,
        IEnumerable<KeyValuePair<string, string>> headers = null)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(path));

        var requestUri = BuildRequestUri(path, PrepareQueryParams(queryParams));

        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = GetJsonContent(data)
        };

        PrepareRequest(request, headers);

        var response = await httpClient.SendAsync(request, ct);
        var msg = await response.Content.ReadAsStringAsync(ct);

        return new ResponseContent<T>(response, Deserialize<T>(msg));
    }

    public async Task<HttpResponseMessage> DeleteWithResponse(string path, CancellationToken ct,
        IEnumerable<KeyValuePair<string, string>> queryParams = null,
        IEnumerable<KeyValuePair<string, string>> headers = null)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(path));

        var requestUri = BuildRequestUri(path, PrepareQueryParams(queryParams));
        var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);

        PrepareRequest(request, headers);

        return await httpClient.SendAsync(request, ct);
    }

    public async Task<T> Delete<T>(string path, CancellationToken ct,
        IEnumerable<KeyValuePair<string, string>> queryParams = null,
        IEnumerable<KeyValuePair<string, string>> headers = null)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(path));

        var requestUri = BuildRequestUri(path, PrepareQueryParams(queryParams));
        var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);

        PrepareRequest(request, headers);

        using var response = await httpClient.SendAsync(request, ct);
        var msg = await response.Content.ReadAsStringAsync(ct);

        if(!response.IsSuccessStatusCode)
            throw new HttpRequestException(msg, null, response.StatusCode);

        return Deserialize<T>(msg);
    }

    public async Task<ResponseContent<T>> DeleteWithResponse<T>(string path, CancellationToken ct,
        IEnumerable<KeyValuePair<string, string>> queryParams = null,
        IEnumerable<KeyValuePair<string, string>> headers = null)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(path));

        var requestUri = BuildRequestUri(path, PrepareQueryParams(queryParams));
        var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);

        PrepareRequest(request, headers);

        var response = await httpClient.SendAsync(request, ct);
        var msg = await response.Content.ReadAsStringAsync(ct);

        return new ResponseContent<T>(response, Deserialize<T>(msg));
    }
}
