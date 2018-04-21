/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Buffers;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.JsonRpc;
using MiningCore.Stratum;
using MiningCore.Util;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.DaemonInterface
{
    /// <summary>
    /// Provides JsonRpc based interface to a cluster of blockchain daemons for improved fault tolerance
    /// </summary>
    public class DaemonClient
    {
        public DaemonClient(JsonSerializerSettings serializerSettings)
        {
            Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));

            this.serializerSettings = serializerSettings;

            serializer = new JsonSerializer
            {
                ContractResolver = serializerSettings.ContractResolver
            };
        }

        private readonly JsonSerializerSettings serializerSettings;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        protected DaemonEndpointConfig[] endPoints;
        private Dictionary<DaemonEndpointConfig, HttpClient> httpClients;
        private readonly JsonSerializer serializer;

        #region API-Surface

        public void Configure(DaemonEndpointConfig[] endPoints, string digestAuthRealm = null)
        {
            Contract.RequiresNonNull(endPoints, nameof(endPoints));
            Contract.Requires<ArgumentException>(endPoints.Length > 0, $"{nameof(endPoints)} must not be empty");

            this.endPoints = endPoints;

            // create one HttpClient instance per endpoint that carries the associated credentials
            httpClients = endPoints.ToDictionary(endpoint => endpoint, endpoint =>
            {
                var handler = new HttpClientHandler
                {
                    Credentials = new NetworkCredential(endpoint.User, endpoint.Password),
                    PreAuthenticate = true,
                    AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
                };

                if (endpoint.Ssl && !endpoint.ValidateCert)
                {
                    handler.ClientCertificateOptions = ClientCertificateOption.Manual;
                    handler.ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true;
                }

                return new HttpClient(handler);
            });
        }

        /// <summary>
        /// Executes the request against all configured demons and returns their responses as an array
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        public Task<DaemonResponse<JToken>[]> ExecuteCmdAllAsync(string method)
        {
            return ExecuteCmdAllAsync<JToken>(method);
        }

        /// <summary>
        /// Executes the request against all configured demons and returns their responses as an array
        /// </summary>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="method"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        public async Task<DaemonResponse<TResponse>[]> ExecuteCmdAllAsync<TResponse>(string method,
            object payload = null, JsonSerializerSettings payloadJsonSerializerSettings = null)
            where TResponse : class
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            logger.LogInvoke(new[] { method });

            var tasks = endPoints.Select(endPoint => BuildRequestTask(endPoint, method, payload, payloadJsonSerializerSettings)).ToArray();

            try
            {
                await Task.WhenAll(tasks);
            }

            catch(Exception)
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
        /// <returns></returns>
        public Task<DaemonResponse<JToken>> ExecuteCmdAnyAsync(string method, bool throwOnError = false)
        {
            return ExecuteCmdAnyAsync<JToken>(method, null, null, throwOnError);
        }

        /// <summary>
        /// Executes the request against all configured demons and returns the first successful response
        /// </summary>
        /// <typeparam name="TResponse"></typeparam>
        /// <returns></returns>
        public async Task<DaemonResponse<TResponse>> ExecuteCmdAnyAsync<TResponse>(string method, object payload = null,
            JsonSerializerSettings payloadJsonSerializerSettings = null, bool throwOnError = false)
            where TResponse : class
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            logger.LogInvoke(new[] { method });

            var tasks = endPoints.Select(endPoint => BuildRequestTask(endPoint, method, payload, payloadJsonSerializerSettings)).ToArray();

            var taskFirstCompleted = await Task.WhenAny(tasks);
            var result = MapDaemonResponse<TResponse>(0, taskFirstCompleted, throwOnError);
            return result;
        }

        /// <summary>
        /// Executes the request against all configured demons and returns the first successful response
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        public Task<DaemonResponse<JToken>> ExecuteCmdSingleAsync(string method)
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
        public async Task<DaemonResponse<TResponse>> ExecuteCmdSingleAsync<TResponse>(string method, object payload = null,
            JsonSerializerSettings payloadJsonSerializerSettings = null)
            where TResponse : class
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            logger.LogInvoke(new[] { method });

            var task = BuildRequestTask(endPoints.First(), method, payload, payloadJsonSerializerSettings);
            await task;

            var result = MapDaemonResponse<TResponse>(0, task);
            return result;
        }

        /// <summary>
        /// Executes the requests against all configured demons and returns the first successful response array
        /// </summary>
        /// <returns></returns>
        public async Task<DaemonResponse<JToken>[]> ExecuteBatchAnyAsync(params DaemonCmd[] batch)
        {
            Contract.RequiresNonNull(batch, nameof(batch));

            logger.LogInvoke(batch.Select(x => x.Method).ToArray());

            var tasks = endPoints.Select(endPoint => BuildBatchRequestTask(endPoint, batch)).ToArray();

            var taskFirstCompleted = await Task.WhenAny(tasks);
            var result = MapDaemonBatchResponse(0, taskFirstCompleted);
            return result;
        }

        public IObservable<PooledArraySegment<byte>> WebsocketSubscribe(Dictionary<DaemonEndpointConfig, 
            (int Port, string HttpPath, bool Ssl)> portMap, string method, object payload = null,
            JsonSerializerSettings payloadJsonSerializerSettings = null)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            logger.LogInvoke(new[] { method });

            return Observable.Merge(portMap.Keys
                    .Select(endPoint => WebsocketSubscribeEndpoint(endPoint, portMap[endPoint], method, payload, payloadJsonSerializerSettings)))
                .Publish()
                .RefCount();
        }

        public IObservable<PooledArraySegment<byte>[]> ZmqSubscribe(Dictionary<DaemonEndpointConfig, (string Socket, string Topic)> portMap, int numMsgSegments = 2)
        {
            logger.LogInvoke();

            return Observable.Merge(portMap.Keys
                    .Select(endPoint => ZmqSubscribeEndpoint(endPoint, portMap[endPoint].Socket, portMap[endPoint].Topic, numMsgSegments)))
                .Publish()
                .RefCount();
        }

        #endregion // API-Surface

        private async Task<JsonRpcResponse> BuildRequestTask(DaemonEndpointConfig endPoint, string method, object payload,
            JsonSerializerSettings payloadJsonSerializerSettings = null)
        {
            var rpcRequestId = GetRequestId();

            // build rpc request
            var rpcRequest = new JsonRpcRequest<object>(method, payload, rpcRequestId);

            // build request url
            var protocol = endPoint.Ssl ? "https" : "http";
            var requestUrl = $"{protocol}://{endPoint.Host}:{endPoint.Port}";
            if (!string.IsNullOrEmpty(endPoint.HttpPath))
                requestUrl += $"{(endPoint.HttpPath.StartsWith("/") ? string.Empty : "/")}{endPoint.HttpPath}";

            // build http request
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            var json = JsonConvert.SerializeObject(rpcRequest, payloadJsonSerializerSettings ?? serializerSettings);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            if(endPoint.Http2)
                request.Version = new Version(2, 0);

            // build auth header
            if (!string.IsNullOrEmpty(endPoint.User))
            {
                var auth = $"{endPoint.User}:{endPoint.Password}";
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
            }

            logger.Trace(()=> $"Sending RPC request to {requestUrl}: {json}");

            // send request
            using(var response = await httpClients[endPoint].SendAsync(request))
            {
                // deserialize response
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        using (var jreader = new JsonTextReader(reader))
                        {
                            var result = serializer.Deserialize<JsonRpcResponse>(jreader);
                            return result;
                        }
                    }

                }
            }
        }

        private async Task<JsonRpcResponse<JToken>[]> BuildBatchRequestTask(DaemonEndpointConfig endPoint, DaemonCmd[] batch)
        {
            // build rpc request
            var rpcRequests = batch.Select(x => new JsonRpcRequest<object>(x.Method, x.Payload, GetRequestId()));

            // build request url
            var protocol = endPoint.Ssl ? "https" : "http";
            var requestUrl = $"{protocol}://{endPoint.Host}:{endPoint.Port}";
            if (!string.IsNullOrEmpty(endPoint.HttpPath))
                requestUrl += $"{(endPoint.HttpPath.StartsWith("/") ? string.Empty : "/")}{endPoint.HttpPath}";

            // build http request
            using(var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
            {
                var json = JsonConvert.SerializeObject(rpcRequests, serializerSettings);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                if (endPoint.Http2)
                    request.Version = new Version(2, 0);

                // build auth header
                if (!string.IsNullOrEmpty(endPoint.User))
                {
                    var auth = $"{endPoint.User}:{endPoint.Password}";
                    var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
                }

                logger.Trace(() => $"Sending RPC request to {requestUrl}: {json}");

                // send request
                using(var response = await httpClients[endPoint].SendAsync(request))
                {
                    // check success
                    if (!response.IsSuccessStatusCode)
                        throw new DaemonClientException(response.StatusCode, response.ReasonPhrase);

                    // deserialize response
                    using(var stream = await response.Content.ReadAsStreamAsync())
                    {
                        using(var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            using(var jreader = new JsonTextReader(reader))
                            {
                                var result = serializer.Deserialize<JsonRpcResponse<JToken>[]>(jreader);
                                return result;
                            }
                        }
                    }
                }
            }
        }

        protected string GetRequestId()
        {
            var rpcRequestId = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + StaticRandom.Next(10)).ToString();
            return rpcRequestId;
        }

        private DaemonResponse<TResponse> MapDaemonResponse<TResponse>(int i, Task<JsonRpcResponse> x, bool throwOnError = false)
            where TResponse : class
        {
            var resp = new DaemonResponse<TResponse>
            {
                Instance = endPoints[i]
            };

            if (x.IsFaulted)
            {
                Exception inner;

                if (x.Exception.InnerExceptions.Count == 1)
                    inner = x.Exception.InnerException;
                else
                    inner = x.Exception;

                if (throwOnError)
                    throw inner;

                resp.Error = new JsonRpcException(-500, x.Exception.Message, null, inner);
            }

            else if (x.IsCanceled)
            {
                resp.Error = new JsonRpcException(-500, "Cancelled", null);
            }

            else
            {
                Debug.Assert(x.IsCompletedSuccessfully);

                if (x.Result?.Result is JToken token)
                    resp.Response = token?.ToObject<TResponse>(serializer);
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

        private IObservable<PooledArraySegment<byte>> WebsocketSubscribeEndpoint(DaemonEndpointConfig endPoint,
            (int Port, string HttpPath, bool Ssl) conf, string method, object payload = null,
            JsonSerializerSettings payloadJsonSerializerSettings = null)
        {
            return Observable.Defer(()=> Observable.Create<PooledArraySegment<byte>>(obs =>
            {
                var cts = new CancellationTokenSource();

                var thread = new Thread(async (_) =>
                {
                    using(cts)
                    {
                        while(!cts.IsCancellationRequested)
                        {
                            try
                            {
                                using (var plb = new PooledLineBuffer(logger))
                                {
                                    using(var client = new ClientWebSocket())
                                    {
                                        // connect
                                        var protocol = conf.Ssl ? "wss" : "ws";
                                        var uri = new Uri($"{protocol}://{endPoint.Host}:{conf.Port}{conf.HttpPath}");

                                        logger.Debug(() => $"Establishing WebSocket connection to {uri}");
                                        await client.ConnectAsync(uri, cts.Token);

                                        // subscribe
                                        var buf = ArrayPool<byte>.Shared.Rent(0x10000);

                                        try
                                        {
                                            var request = new JsonRpcRequest(method, payload, GetRequestId());
                                            var json = JsonConvert.SerializeObject(request, payloadJsonSerializerSettings).ToCharArray();
                                            var byteLength = Encoding.UTF8.GetBytes(json, 0, json.Length, buf, 0);
                                            var segment = new ArraySegment<byte>(buf, 0, byteLength);

                                            logger.Debug(() => $"Sending WebSocket subscription request to {uri}");
                                            await client.SendAsync(segment, WebSocketMessageType.Text, true, cts.Token);

                                            // stream response
                                            segment = new ArraySegment<byte>(buf);

                                            while (!cts.IsCancellationRequested && client.State == WebSocketState.Open)
                                            {
                                                var response = await client.ReceiveAsync(buf, cts.Token);

                                                if (response.MessageType == WebSocketMessageType.Binary)
                                                    throw new InvalidDataException("expected text, received binary data");

                                                plb.Receive(segment, response.Count,
                                                    (src, dst, count) => Array.Copy(src.Array, src.Offset, dst, 0, count),
                                                    obs.OnNext, (ex) => { }, response.EndOfMessage);
                                            }
                                        }

                                        finally
                                        {
                                            ArrayPool<byte>.Shared.Return(buf);
                                        }
                                    }
                                }
                            }

                            catch (Exception ex)
                            {
                                logger.Error(()=> $"{ex.GetType().Name} '{ex.Message}' while streaming websocket responses. Reconnecting in 5s");
                            }

                            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                        }
                    }
                });

                thread.Start();

                return Disposable.Create(() =>
                {
                    cts.Cancel();
                });
            }));
        }

        private IObservable<PooledArraySegment<byte>[]> ZmqSubscribeEndpoint(DaemonEndpointConfig endPoint, string url, string topic, int numMsgSegments = 2)
        {
            return Observable.Defer(() => Observable.Create<PooledArraySegment<byte>[]>(obs =>
            {
                var tcs = new CancellationTokenSource();

                Task.Factory.StartNew(() =>
                {
                    using (tcs)
                    {
                        while (!tcs.IsCancellationRequested)
                        {
                            try
                            {
                                using (var subSocket = new SubscriberSocket())
                                {
                                    //subSocket.Options.ReceiveHighWatermark = 1000;
                                    subSocket.Connect(url);
                                    subSocket.Subscribe(topic);

                                    logger.Debug($"Subscribed to {url}/{topic}");

                                    while (!tcs.IsCancellationRequested)
                                    {
                                        var msg = subSocket.ReceiveMultipartMessage(numMsgSegments);

                                        // Export all frame data as array of PooledArraySegments
                                        var result = msg.Select(x =>
                                        {
                                            var buf = ArrayPool<byte>.Shared.Rent(x.BufferSize);
                                            Array.Copy(x.ToByteArray(), buf, x.BufferSize);
                                            return new PooledArraySegment<byte>(buf, 0, x.BufferSize);
                                        }).ToArray();

                                        obs.OnNext(result);
                                    }
                                }
                            }

                            catch (Exception ex)
                            {
                                logger.Error(ex);
                            }

                            // do not consume all CPU cycles in case of a long lasting error condition
                            Thread.Sleep(1000);
                        }
                    }
                }, tcs.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                return Disposable.Create(() =>
                {
                    tcs.Cancel();
                });
            }));
        }
    }
}
