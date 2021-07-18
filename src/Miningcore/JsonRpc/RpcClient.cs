using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using ZeroMQ;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.JsonRpc
{
    /// <summary>
    /// JsonRpc interface to blockchain node
    /// </summary>
    public class RpcClient
    {
        public RpcClient(DaemonEndpointConfig endPoint, JsonSerializerSettings serializerSettings, IMessageBus messageBus, string poolId)
        {
            Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(poolId), $"{nameof(poolId)} must not be empty");

            this.endPoint = endPoint;
            this.serializerSettings = serializerSettings;
            this.messageBus = messageBus;
            this.poolId = poolId;

            serializer = new JsonSerializer
            {
                ContractResolver = serializerSettings.ContractResolver
            };
        }

        private readonly JsonSerializerSettings serializerSettings;

        protected readonly DaemonEndpointConfig endPoint;
        private readonly JsonSerializer serializer;

        private static readonly HttpClient httpClient = new(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,

            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
        });

        // Telemetry
        private readonly IMessageBus messageBus;

        private readonly string poolId;

        protected void PublishTelemetry(TelemetryCategory cat, TimeSpan elapsed, string info, bool? success = null, string error = null)
        {
            messageBus.SendMessage(new TelemetryEvent(poolId, cat, info, elapsed, success));
        }

        #region API-Surface

        public async Task<RpcResponse<TResponse>> ExecuteAsync<TResponse>(ILogger logger, string method, CancellationToken ct,
            object payload = null, bool throwOnError = false)
            where TResponse : class
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            logger.LogInvoke(new object[] { method });

            try
            {
                var response = await RequestAsync(logger, ct, endPoint, method, payload);

                if(response.Result is JToken token)
                    return new RpcResponse<TResponse>(token.ToObject<TResponse>(serializer), response.Error);

                return new RpcResponse<TResponse>((TResponse) response.Result, response.Error);
            }

            catch(TaskCanceledException)
            {
                return new RpcResponse<TResponse>(null, new JsonRpcError(-500, "Cancelled", null));
            }

            catch(Exception ex)
            {
                if(throwOnError)
                    throw;

                return new RpcResponse<TResponse>(null, new JsonRpcError(-500, ex.Message, null, ex));
            }
        }

        public Task<RpcResponse<JToken>> ExecuteAsync(ILogger logger, string method, CancellationToken ct, bool throwOnError = false)
        {
            return ExecuteAsync<JToken>(logger, method, ct, null, throwOnError);
        }

        public async Task<RpcResponse<JToken>[]> ExecuteBatchAsync(ILogger logger, CancellationToken ct, params RpcRequest[] batch)
        {
            Contract.RequiresNonNull(batch, nameof(batch));

            logger.LogInvoke(string.Join(", ", batch.Select(x=> x.Method)));

            try
            {
                var response = await BatchRequestAsync(logger, ct, endPoint, batch);

                return response
                    .Select(x => new RpcResponse<JToken>(x.Result != null ? JToken.FromObject(x.Result) : null, x.Error))
                    .ToArray();
            }

            catch(Exception ex)
            {
                return Enumerable.Repeat(new RpcResponse<JToken>(null, new JsonRpcError(-500, ex.Message, null, ex)), batch.Length).ToArray();
            }
        }

        public IObservable<byte[]> WebsocketSubscribe(ILogger logger, CancellationToken ct, Dictionary<DaemonEndpointConfig,
                (int Port, string HttpPath, bool Ssl)> portMap, string method, object payload = null,
            JsonSerializerSettings payloadJsonSerializerSettings = null)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            logger.LogInvoke(new object[] { method });

            return Observable.Merge(portMap.Keys
                    .Select(endPoint => WebsocketSubscribeEndpoint(logger, ct, endPoint, portMap[endPoint], method, payload, payloadJsonSerializerSettings)))
                .Publish()
                .RefCount();
        }

        public IObservable<ZMessage> ZmqSubscribe(ILogger logger, CancellationToken ct, Dictionary<DaemonEndpointConfig, (string Socket, string Topic)> portMap)
        {
            logger.LogInvoke();

            return Observable.Merge(portMap.Keys
                    .Select(endPoint => ZmqSubscribeEndpoint(logger, ct, endPoint, portMap[endPoint].Socket, portMap[endPoint].Topic)))
                .Publish()
                .RefCount();
        }

        #endregion // API-Surface

        private async Task<JsonRpcResponse> RequestAsync(ILogger logger, CancellationToken ct, DaemonEndpointConfig endPoint, string method, object payload)
        {
            var rpcRequestId = GetRequestId();

            // telemetry
            var sw = Stopwatch.StartNew();

            // build rpc request
            var rpcRequest = new JsonRpcRequest<object>(method, payload, rpcRequestId);

            // build url
            var protocol = (endPoint.Ssl || endPoint.Http2) ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            var requestUrl = $"{protocol}://{endPoint.Host}:{endPoint.Port}";
            if(!string.IsNullOrEmpty(endPoint.HttpPath))
                requestUrl += $"{(endPoint.HttpPath.StartsWith("/") ? string.Empty : "/")}{endPoint.HttpPath}";

            // build request
            using(var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
            {
                if(endPoint.Http2)
                    request.Version = new Version(2, 0);
                else
                    request.Headers.ConnectionClose = false;    // enable keep-alive

                // build content
                var json = JsonConvert.SerializeObject(rpcRequest, serializerSettings);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                // build auth header
                if(!string.IsNullOrEmpty(endPoint.User))
                {
                    var auth = $"{endPoint.User}:{endPoint.Password}";
                    var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
                }

                logger.Trace(() => $"Sending RPC request to {requestUrl}: {json}");

                // send request
                using(var response = await httpClient.SendAsync(request, ct))
                {
                    // read response
                    var responseContent = await response.Content.ReadAsStringAsync(ct);

                    // deserialize response
                    using(var jreader = new JsonTextReader(new StringReader(responseContent)))
                    {
                        var result = serializer.Deserialize<JsonRpcResponse>(jreader);

                        // telemetry
                        sw.Stop();
                        PublishTelemetry(TelemetryCategory.RpcRequest, sw.Elapsed, method, response.IsSuccessStatusCode);

                        return result;
                    }
                }
            }
        }

        private async Task<JsonRpcResponse<JToken>[]> BatchRequestAsync(ILogger logger, CancellationToken ct, DaemonEndpointConfig endPoint, RpcRequest[] batch)
        {
            // telemetry
            var sw = Stopwatch.StartNew();

            // build rpc request
            var rpcRequests = batch.Select(x => new JsonRpcRequest<object>(x.Method, x.Payload, GetRequestId()));

            // build url
            var protocol = (endPoint.Ssl || endPoint.Http2) ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            var requestUrl = $"{protocol}://{endPoint.Host}:{endPoint.Port}";
            if(!string.IsNullOrEmpty(endPoint.HttpPath))
                requestUrl += $"{(endPoint.HttpPath.StartsWith("/") ? string.Empty : "/")}{endPoint.HttpPath}";

            // build request
            using(var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
            {
                if(endPoint.Http2)
                    request.Version = new Version(2, 0);
                else
                    request.Headers.ConnectionClose = false;    // enable keep-alive

                // build request content
                var json = JsonConvert.SerializeObject(rpcRequests, serializerSettings);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                // build auth header
                if(!string.IsNullOrEmpty(endPoint.User))
                {
                    var auth = $"{endPoint.User}:{endPoint.Password}";
                    var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
                }

                logger.Trace(() => $"Sending RPC request to {requestUrl}: {json}");

                // send request
                using(var response = await httpClient.SendAsync(request, ct))
                {
                    // deserialize response
                    var jsonResponse = await response.Content.ReadAsStringAsync(ct);

                    using(var jreader = new JsonTextReader(new StringReader(jsonResponse)))
                    {
                        var result = serializer.Deserialize<JsonRpcResponse<JToken>[]>(jreader);

                        // telemetry
                        sw.Stop();
                        PublishTelemetry(TelemetryCategory.RpcRequest, sw.Elapsed, string.Join(", ", batch.Select(x => x.Method)), true);

                        return result;
                    }
                }
            }
        }

        protected string GetRequestId()
        {
            var rpcRequestId = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + StaticRandom.Next(10)).ToString();
            return rpcRequestId;
        }

        private IObservable<byte[]> WebsocketSubscribeEndpoint(ILogger logger, CancellationToken ct, NetworkEndpointConfig endPoint,
            (int Port, string HttpPath, bool Ssl) conf, string method, object payload = null,
            JsonSerializerSettings payloadJsonSerializerSettings = null)
        {
            return Observable.Defer(() => Observable.Create<byte[]>(obs =>
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                Task.Run(async () =>
                {
                    using(cts)
                    {
                        var buf = new byte[0x10000];

                        while(!cts.IsCancellationRequested)
                        {
                            try
                            {
                                using(var client = new ClientWebSocket())
                                {
                                    // connect
                                    var protocol = conf.Ssl ? "wss" : "ws";
                                    var uri = new Uri($"{protocol}://{endPoint.Host}:{conf.Port}{conf.HttpPath}");
                                    client.Options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

                                    logger.Debug(() => $"Establishing WebSocket connection to {uri}");
                                    await client.ConnectAsync(uri, cts.Token);

                                    // subscribe
                                    var request = new JsonRpcRequest(method, payload, GetRequestId());
                                    var json = JsonConvert.SerializeObject(request, payloadJsonSerializerSettings);
                                    var requestData = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));

                                    logger.Debug(() => $"Sending WebSocket subscription request to {uri}");
                                    await client.SendAsync(requestData, WebSocketMessageType.Text, true, cts.Token);

                                    // stream response
                                    var stream = new MemoryStream();

                                    while(!cts.IsCancellationRequested && client.State == WebSocketState.Open)
                                    {
                                        stream.SetLength(0);
                                        var complete = false;

                                        // read until EndOfMessage
                                        do
                                        {
                                            using(var ctsTimeout = new CancellationTokenSource())
                                            {
                                                using(var ctsComposite = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ctsTimeout.Token))
                                                {
                                                    ctsTimeout.CancelAfter(TimeSpan.FromMinutes(10));

                                                    var response = await client.ReceiveAsync(buf, ctsComposite.Token);

                                                    if(response.MessageType == WebSocketMessageType.Binary)
                                                        throw new InvalidDataException("expected text, received binary data");

                                                    await stream.WriteAsync(buf, 0, response.Count, ctsComposite.Token);

                                                    complete = response.EndOfMessage;
                                                }
                                            }
                                        } while(!complete && !cts.IsCancellationRequested && client.State == WebSocketState.Open);

                                        logger.Debug(() => $"Received WebSocket message with length {stream.Length}");

                                        // publish
                                        obs.OnNext(stream.ToArray());
                                    }
                                }
                            }

                            catch(Exception ex)
                            {
                                logger.Error(() => $"{ex.GetType().Name} '{ex.Message}' while streaming websocket responses. Reconnecting in 5s");
                            }

                            if(!cts.IsCancellationRequested)
                                await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                        }
                    }
                }, cts.Token);

                return Disposable.Create(() => { cts.Cancel(); });
            }));
        }

        private static IObservable<ZMessage> ZmqSubscribeEndpoint(ILogger logger, CancellationToken ct, DaemonEndpointConfig endPoint, string url, string topic)
        {
            return Observable.Defer(() => Observable.Create<ZMessage>(obs =>
            {
                var tcs = CancellationTokenSource.CreateLinkedTokenSource(ct);

                Task.Run(() =>
                {
                    using(tcs)
                    {
                        while(!tcs.IsCancellationRequested)
                        {
                            try
                            {
                                using(var subSocket = new ZSocket(ZSocketType.SUB))
                                {
                                    //subSocket.Options.ReceiveHighWatermark = 1000;
                                    subSocket.Connect(url);
                                    subSocket.Subscribe(topic);

                                    logger.Debug($"Subscribed to {url}/{topic}");

                                    while(!tcs.IsCancellationRequested)
                                    {
                                        var msg = subSocket.ReceiveMessage();
                                        obs.OnNext(msg);
                                    }
                                }
                            }

                            catch(Exception ex)
                            {
                                logger.Error(ex);
                            }

                            // do not consume all CPU cycles in case of a long lasting error condition
                            Thread.Sleep(1000);
                        }
                    }
                }, tcs.Token);

                return Disposable.Create(() => { tcs.Cancel(); });
            }));
        }
    }
}
