using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using CodeContracts;
using MiningForce.Configuration;
using MiningForce.JsonRpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiningForce.Blockchain
{
    public class DaemonResponse<T>
    {
        public JsonRpcException Error { get; set; }
        public T Response { get; set; }
        public AuthenticatedNetworkEndpointConfig Instance { get; set; }
    }

    public class BlockchainDaemon
    {
        public BlockchainDaemon(HttpClient httpClient, JsonSerializerSettings serializerSettings)
        {
            this.httpClient = httpClient;
            this.serializerSettings = serializerSettings;
        }

        protected AuthenticatedNetworkEndpointConfig[] endPoints;
        private readonly Random random = new Random();
        protected HttpClient httpClient;
        private readonly JsonSerializerSettings serializerSettings;

        #region API-Surface

        public void Start(PoolConfig poolConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.Requires<ArgumentException>(poolConfig.Daemons.Length > 0, $"{nameof(poolConfig.Daemons)} must not be empty");

            this.endPoints = poolConfig.Daemons;
        }

        /// <summary>
        /// Executes the request against all configured demons and returns their responses as an array
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        public Task<DaemonResponse<JToken>[]> ExecuteCmdAllAsync(string method)
        {
            return ExecuteCmdAllAsync<JToken> (method);
        }

        /// <summary>
        /// Executes the request against all configured demons and returns their responses as an array
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="method"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        public async Task<DaemonResponse<TResponse>[]> ExecuteCmdAllAsync<TResponse>(string method, object payload = null)
            where TResponse: class
        {
            var tasks = endPoints.Select(endPoint=> BuildRequestTask(endPoint, method, payload)).ToArray();

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
        /// Executes the request against all configured demons and returns the first successful response
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        public Task<DaemonResponse<JToken>> ExecuteCmdAnyAsync(string method)
        {
            return ExecuteCmdAnyAsync<JToken>(method);
        }

        /// <summary>
        /// Executes the request against all configured demons and returns the first successful response
        /// </summary>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="method"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        public async Task<DaemonResponse<TResponse>> ExecuteCmdAnyAsync<TResponse>(string method, object payload = null)
            where TResponse : class
        {
            var tasks = endPoints.Select(endPoint => BuildRequestTask(endPoint, method, payload)).ToArray();

            var taskFirstCompleted = await Task.WhenAny(tasks);
            var result = MapDaemonResponse<TResponse>(0, taskFirstCompleted);
            return result;
        }

        private async Task<JsonRpcResponse> BuildRequestTask(
            AuthenticatedNetworkEndpointConfig endPoint, string method, object payload) 
        {
            var rpcRequestId = GetRequestId();

            // build rpc request
            var rpcRequest = new JsonRpcRequest<object>(method, payload, rpcRequestId);

            // build http request
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://{endPoint.Host}:{endPoint.Port}");
            var json = JsonConvert.SerializeObject(rpcRequest, serializerSettings);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            // build auth header
            var auth = $"{endPoint.User}:{endPoint.Password}";
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);

            // send request
            var response = await httpClient.SendAsync(request);
            json = await response.Content.ReadAsStringAsync();

            // deserialize response
            var result = JsonConvert.DeserializeObject<JsonRpcResponse>(json);
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

            if (x.IsCompletedSuccessfully)
            {
                resp.Response = x.Result?.Result?.ToObject<TResponse>();
                resp.Error = x.Result?.Error;
            }

            else if (x.IsFaulted)
            {
                resp.Error = new JsonRpcException(-500, x.Exception.Message, null);
            }

            return resp;
        }

        #endregion // API-Surface
    }
}
