using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using CodeContracts;
using MiningCore.Configuration;
using MiningCore.JsonRpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiningCore.DaemonInterface
{
    /// <summary>
    ///     Provides JsonRpc based interface to a cluster of blockchain daemons for improved fault tolerance
    /// </summary>
    public class DaemonClient
    {
        private readonly Random random = new Random();
        private readonly JsonSerializerSettings serializerSettings;

        protected DaemonEndpointConfig[] endPoints;
        private Dictionary<DaemonEndpointConfig, HttpClient> httpClients;
        private string rpcLocation;

        public DaemonClient(JsonSerializerSettings serializerSettings)
        {
            Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));

            this.serializerSettings = serializerSettings;
        }

        #region API-Surface

        public void Configure(DaemonEndpointConfig[] endPoints, string rpcLocation = null,
            string digestAuthRealm = null)
        {
            Contract.RequiresNonNull(endPoints, nameof(endPoints));
            Contract.Requires<ArgumentException>(endPoints.Length > 0, $"{nameof(endPoints)} must not be empty");

            this.endPoints = endPoints;
            this.rpcLocation = rpcLocation;

            // create one HttpClient instance per endpoint that carries the associated credentials
            httpClients = endPoints.ToDictionary(endpoint => endpoint, endpoint =>
                new HttpClient(new HttpClientHandler
                {
                    Credentials = new NetworkCredential(endpoint.User, endpoint.Password),
                    PreAuthenticate = true,
                    AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
                }));
        }

        /// <summary>
        ///     Executes the request against all configured demons and returns their responses as an array
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        public Task<DaemonResponse<JToken>[]> ExecuteCmdAllAsync(string method)
        {
            return ExecuteCmdAllAsync<JToken>(method);
        }

        /// <summary>
        ///     Executes the request against all configured demons and returns their responses as an array
        /// </summary>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="method"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        public async Task<DaemonResponse<TResponse>[]> ExecuteCmdAllAsync<TResponse>(string method,
            object payload = null)
            where TResponse : class
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            var tasks = endPoints.Select(endPoint => BuildRequestTask(endPoint, method, payload)).ToArray();

            try
            {
                await Task.WhenAll(tasks);
            }

            catch (Exception)
            {
                // ignored
            }

            var results = tasks.Select((x, i) => MapDaemonResponse<TResponse>(i, x))
                .ToArray();

            return results;
        }

        /// <summary>
        ///     Executes the request against all configured demons and returns the first successful response
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        public Task<DaemonResponse<JToken>> ExecuteCmdAnyAsync(string method)
        {
            return ExecuteCmdAnyAsync<JToken>(method);
        }

        /// <summary>
        ///     Executes the request against all configured demons and returns the first successful response
        /// </summary>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="method"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        public async Task<DaemonResponse<TResponse>> ExecuteCmdAnyAsync<TResponse>(string method, object payload = null)
            where TResponse : class
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            var tasks = endPoints.Select(endPoint => BuildRequestTask(endPoint, method, payload)).ToArray();

            var taskFirstCompleted = await Task.WhenAny(tasks);
            var result = MapDaemonResponse<TResponse>(0, taskFirstCompleted);
            return result;
        }

        /// <summary>
        ///     Executes the requests against all configured demons and returns the first successful response array
        /// </summary>
        /// <returns></returns>
        public async Task<DaemonResponse<JToken>[]> ExecuteBatchAnyAsync(params DaemonCmd[] batch)
        {
            Contract.RequiresNonNull(batch, nameof(batch));

            var tasks = endPoints.Select(endPoint => BuildBatchRequestTask(endPoint, batch)).ToArray();

            var taskFirstCompleted = await Task.WhenAny(tasks);
            var result = MapDaemonBatchResponse(0, taskFirstCompleted);
            return result;
        }

        private async Task<JsonRpcResponse> BuildRequestTask(DaemonEndpointConfig endPoint, string method,
            object payload)
        {
            var rpcRequestId = GetRequestId();

            // build rpc request
            var rpcRequest = new JsonRpcRequest<object>(method, payload, rpcRequestId);

            // build request url
            var requestUrl = $"http://{endPoint.Host}:{endPoint.Port}";
            if (!string.IsNullOrEmpty(rpcLocation))
                requestUrl += $"/{rpcLocation}";

            // build http request
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            var json = JsonConvert.SerializeObject(rpcRequest, serializerSettings);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            // build auth header
            if (!string.IsNullOrEmpty(endPoint.User))
            {
                var auth = $"{endPoint.User}:{endPoint.Password}";
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
            }

            // send request
            var httpClient = httpClients[endPoint];
            var response = await httpClient.SendAsync(request);
            json = await response.Content.ReadAsStringAsync();

            // deserialize response
            var result = JsonConvert.DeserializeObject<JsonRpcResponse>(json);
            return result;
        }

        private async Task<JsonRpcResponse<JToken>[]> BuildBatchRequestTask(DaemonEndpointConfig endPoint,
            DaemonCmd[] batch)
        {
            // build rpc request
            var rpcRequests = batch.Select(x => new JsonRpcRequest<object>(x.Method, x.Payload, GetRequestId()));

            // build request url
            var requestUrl = $"http://{endPoint.Host}:{endPoint.Port}";
            if (!string.IsNullOrEmpty(rpcLocation))
                requestUrl += $"/{rpcLocation}";

            // build http request
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            var json = JsonConvert.SerializeObject(rpcRequests, serializerSettings);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            // build auth header
            if (!string.IsNullOrEmpty(endPoint.User))
            {
                var auth = $"{endPoint.User}:{endPoint.Password}";
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
            }

            // send request
            var httpClient = httpClients[endPoint];
            var response = await httpClient.SendAsync(request);
            json = await response.Content.ReadAsStringAsync();

            // deserialize response
            var result = JsonConvert.DeserializeObject<JsonRpcResponse<JToken>[]>(json);
            return result;
        }

        protected string GetRequestId()
        {
            string rpcRequestId;

            lock (random)
            {
                rpcRequestId = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + random.Next(10)).ToString();
            }

            return rpcRequestId;
        }

        private DaemonResponse<TResponse> MapDaemonResponse<TResponse>(int i, Task<JsonRpcResponse> x)
            where TResponse : class
        {
            var resp = new DaemonResponse<TResponse>
            {
                Instance = endPoints[i]
            };

            if (x.IsFaulted)
            {
                resp.Error = new JsonRpcException(-500, x.Exception.Message, null);
            }

            else
            {
                Debug.Assert(x.IsCompletedSuccessfully);

                if (x.Result?.Result is JToken)
                    resp.Response = ((JToken) x.Result?.Result)?.ToObject<TResponse>();
                else
                    resp.Response = (TResponse) x.Result?.Result;

                resp.Error = x.Result?.Error;
            }

            return resp;
        }

        private DaemonResponse<JToken>[] MapDaemonBatchResponse(int i, Task<JsonRpcResponse<JToken>[]> x)
        {
            if (x.IsFaulted)
                return x.Result?.Select(y => new DaemonResponse<JToken>
                {
                    Instance = endPoints[i],
                    Error = new JsonRpcException(-500, x.Exception.Message, null)
                }).ToArray();

            Debug.Assert(x.IsCompletedSuccessfully);

            return x.Result?.Select(y => new DaemonResponse<JToken>
            {
                Instance = endPoints[i],
                Response = y.Result != null ? JToken.FromObject(y.Result) : null,
                Error = y.Error
            }).ToArray();
        }

        #endregion // API-Surface
    }
}
