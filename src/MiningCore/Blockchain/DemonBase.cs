using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using CodeContracts;
using MiningCore.Blockchain.Bitcoin.Messages;
using MiningCore.Configuration;
using MiningCore.JsonRpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiningCore.Blockchain
{
    public class DaemonResponse<T>
    {
        public JsonRpcException Error { get; set; }
        public T Response { get; set; }
        public AuthenticatedNetworkEndpointConfig Instance { get; set; }
    }

    public abstract class DemonBase
    {
        protected DemonBase(HttpClient httpClient, JsonSerializerSettings serializerSettings)
        {
            this.httpClient = httpClient;
            this.serializerSettings = serializerSettings;
        }

        protected AuthenticatedNetworkEndpointConfig[] endPoints;
        private readonly Random random = new Random();
        protected HttpClient httpClient;
        private readonly JsonSerializerSettings serializerSettings;
        protected string testInstanceOnlineCommand;

        /// <summary>
        /// Executes the request against all configured demons and returns their responses as an array
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        protected Task<DaemonResponse<JToken>[]> ExecuteCmdAllAsync(string method)
        {
            return ExecuteCmdAllAsync<object, JToken> (method);
        }

        /// <summary>
        /// Executes the request against all configured demons and returns their responses as an array
        /// </summary>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="method"></param>
        /// <returns></returns>
        protected Task<DaemonResponse<TResponse>[]> ExecuteCmdAllAsync<TResponse>(string method)
            where TResponse : class
        {
            return ExecuteCmdAllAsync<object, TResponse>(method);
        }

        /// <summary>
        /// Executes the request against all configured demons and returns their responses as an array
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="method"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        protected async Task<DaemonResponse<TResponse>[]> ExecuteCmdAllAsync<TRequest, TResponse>(string method, TRequest payload = null)
            where TResponse: class
            where TRequest: class
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
        protected Task<DaemonResponse<JToken>> ExecuteCmdAnyAsync(string method)
        {
            return ExecuteCmdAnyAsync<object, JToken>(method);
        }

        /// <summary>
        /// Executes the request against all configured demons and returns the first successful response
        /// </summary>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="method"></param>
        /// <returns></returns>
        protected Task<DaemonResponse<TResponse>> ExecuteCmdAnyAsync<TResponse>(string method)
            where TResponse : class
        {
            return ExecuteCmdAnyAsync<object, TResponse>(method);
        }

        /// <summary>
        /// Executes the request against all configured demons and returns the first successful response
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="method"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        protected async Task<DaemonResponse<TResponse>> ExecuteCmdAnyAsync<TRequest, TResponse>(string method, TRequest payload = null)
            where TResponse : class
            where TRequest : class
        {
            var tasks = endPoints.Select(endPoint => BuildRequestTask(endPoint, method, payload)).ToArray();

            var taskFirstCompleted = await Task.WhenAny(tasks);
            var result = MapDaemonResponse<TResponse>(0, taskFirstCompleted);
            return result;
        }

        private async Task<JsonRpcResponse> BuildRequestTask<TRequest>(
            AuthenticatedNetworkEndpointConfig endPoint, string method, TRequest payload) 
            where TRequest: class
        {
            var rpcRequestId = GetRequestId();

            // build rpc request
            var rpcRequest = new JsonRpcRequest<TRequest>(method, payload, rpcRequestId);

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

        public async Task<bool> IsHealthyAsync()
        {
            Contract.Requires<ArgumentException>(testInstanceOnlineCommand != null, $"{nameof(testInstanceOnlineCommand)} must be initialized in derived class");

            var responses = await ExecuteCmdAllAsync<GetInfoResponse>(testInstanceOnlineCommand);

            return responses.All(x => x.Error == null);
        }
    }
}
