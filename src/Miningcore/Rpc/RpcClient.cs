using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using ZeroMQ;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Rpc;

/// <summary>
/// JsonRpc interface to blockchain node
/// </summary>
public class RpcClient
{
    public RpcClient(DaemonEndpointConfig endPoint, JsonSerializerSettings serializerSettings, IMessageBus messageBus, string poolId)
    {
        Contract.RequiresNonNull(serializerSettings);
        Contract.RequiresNonNull(messageBus);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(poolId));

        config = endPoint;
        this.serializerSettings = serializerSettings;
        this.messageBus = messageBus;
        this.poolId = poolId;

        serializer = new JsonSerializer
        {
            ContractResolver = serializerSettings.ContractResolver!
        };
    }

    private readonly JsonSerializerSettings serializerSettings;
    protected readonly DaemonEndpointConfig config;
    private readonly JsonSerializer serializer;
    private readonly IMessageBus messageBus;
    private readonly string poolId;

    private static readonly HttpClient httpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,

        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
    });

    #region API-Surface

    public async Task<RpcResponse<TResponse>> ExecuteAsync<TResponse>(ILogger logger, string method, CancellationToken ct,
        object payload = null, bool throwOnError = false)
        where TResponse : class
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method));

        try
        {
            var response = await RequestAsync(logger, ct, config, method, payload);

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
        Contract.RequiresNonNull(batch);

        try
        {
            var response = await BatchRequestAsync(logger, ct, config, batch);

            return response
                .Select(x => new RpcResponse<JToken>(x.Result != null ? JToken.FromObject(x.Result) : null, x.Error))
                .ToArray();
        }

        catch(Exception ex)
        {
            return Enumerable.Repeat(new RpcResponse<JToken>(null, new JsonRpcError(-500, ex.Message, null, ex)), batch.Length).ToArray();
        }
    }

    public IObservable<byte[]> WebsocketSubscribe(ILogger logger, CancellationToken ct, DaemonEndpointConfig endPoint,
        string method, object payload = null,
        JsonSerializerSettings payloadJsonSerializerSettings = null)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(method));

        return WebsocketSubscribeEndpoint(logger, ct, endPoint, method, payload, payloadJsonSerializerSettings)
            .Publish()
            .RefCount();
    }

    public IObservable<ZMessage> ZmqSubscribe(ILogger logger, CancellationToken ct, Dictionary<DaemonEndpointConfig, (string Socket, string Topic)> portMap)
    {
        return portMap.Keys
            .Select(endPoint => ZmqSubscribeEndpoint(logger, ct, portMap[endPoint].Socket, portMap[endPoint].Topic))
            .Merge()
            .Publish()
            .RefCount();
    }

    #endregion // API-Surface

    private async Task<JsonRpcResponse> RequestAsync(ILogger logger, CancellationToken ct, DaemonEndpointConfig endPoint, string method, object payload)
    {
        var sw = Stopwatch.StartNew();

        // build rpc request
        var rpcRequest = new JsonRpcRequest<object>(method, payload, GetRequestId());

        // build url
        var protocol = endPoint.Ssl || endPoint.Http2 ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        var requestUrl = $"{protocol}://{endPoint.Host}:{endPoint.Port}";

        if(!string.IsNullOrEmpty(endPoint.HttpPath))
            requestUrl += $"{(endPoint.HttpPath.StartsWith("/") ? string.Empty : "/")}{endPoint.HttpPath}";

        using(var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
        {
            if(endPoint.Http2)
                request.Version = new Version(2, 0);
            else
                request.Headers.ConnectionClose = false;    // enable keep-alive

            // content
            var json = JsonConvert.SerializeObject(rpcRequest, serializerSettings);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            // auth header
            if(!string.IsNullOrEmpty(endPoint.User))
            {
                var auth = $"{endPoint.User}:{endPoint.Password}";
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth.ToByteArrayBase64());
            }

            logger.Trace(() => $"Sending RPC request to {requestUrl}: {json}");

            // send request
            using(var response = await httpClient.SendAsync(request, ct))
            {
                // read response
                var responseContent = await response.Content.ReadAsStringAsync(ct);

                logger.Trace(() => $"Received RPC response: {responseContent}");

                // deserialize response
                using(var jreader = new JsonTextReader(new StringReader(responseContent)))
                {
                    var result = serializer.Deserialize<JsonRpcResponse>(jreader);

                    messageBus.SendTelemetry(poolId, TelemetryCategory.RpcRequest, method, sw.Elapsed, response.IsSuccessStatusCode);

                    return result;
                }
            }
        }
    }

    private async Task<JsonRpcResponse<JToken>[]> BatchRequestAsync(ILogger logger, CancellationToken ct, DaemonEndpointConfig endPoint, RpcRequest[] batch)
    {
        var sw = Stopwatch.StartNew();

        var rpcRequests = batch.Select(x => new JsonRpcRequest<object>(x.Method, x.Payload, GetRequestId()));

        // url
        var protocol = (endPoint.Ssl || endPoint.Http2) ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        var requestUrl = $"{protocol}://{endPoint.Host}:{endPoint.Port}";

        if(!string.IsNullOrEmpty(endPoint.HttpPath))
            requestUrl += $"{(endPoint.HttpPath.StartsWith("/") ? string.Empty : "/")}{endPoint.HttpPath}";

        using(var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
        {
            if(endPoint.Http2)
                request.Version = new Version(2, 0);
            else
                request.Headers.ConnectionClose = false;    // enable keep-alive

            // content
            var json = JsonConvert.SerializeObject(rpcRequests, serializerSettings);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            // auth header
            if(!string.IsNullOrEmpty(endPoint.User))
            {
                var auth = $"{endPoint.User}:{endPoint.Password}";
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth.ToByteArrayBase64());
            }

            logger.Trace(() => $"Sending RPC request to {requestUrl}: {json}");

            // send request
            using(var response = await httpClient.SendAsync(request, ct))
            {
                // deserialize response
                var responseContent = await response.Content.ReadAsStringAsync(ct);

                logger.Trace(() => $"Received RPC response: {responseContent}");

                using(var jreader = new JsonTextReader(new StringReader(responseContent)))
                {
                    var result = serializer.Deserialize<JsonRpcResponse<JToken>[]>(jreader);

                    messageBus.SendTelemetry(poolId, TelemetryCategory.RpcRequest, string.Join(", ", batch.Select(x => x.Method)),
                        sw.Elapsed, response.IsSuccessStatusCode);

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

    private IObservable<byte[]> WebsocketSubscribeEndpoint(ILogger logger, CancellationToken ct,
        DaemonEndpointConfig endPoint, string method, object payload = null,
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
                                var protocol = endPoint.Ssl ? "wss" : "ws";
                                var uri = new Uri($"{protocol}://{endPoint.Host}:{endPoint.Port}{endPoint.HttpPath}");
                                client.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

                                logger.Debug(() => $"Establishing WebSocket connection to {uri}");
                                await client.ConnectAsync(uri, cts.Token);

                                // subscribe
                                var request = new JsonRpcRequest(method, payload, GetRequestId());
                                var json = JsonConvert.SerializeObject(request, payloadJsonSerializerSettings);
                                var requestData = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));

                                logger.Debug(() => $"Sending WebSocket subscription request to {uri}");
                                await client.SendAsync(requestData, WebSocketMessageType.Text, true, cts.Token);

                                // stream response
                                while(!cts.IsCancellationRequested && client.State == WebSocketState.Open)
                                {
                                    await using var stream = new MemoryStream();

                                    do
                                    {
                                        var response = await client.ReceiveAsync(buf, cts.Token);

                                        if(response.MessageType == WebSocketMessageType.Binary)
                                            throw new InvalidDataException("expected text, received binary data");

                                        await stream.WriteAsync(buf, 0, response.Count, cts.Token);

                                        if(response.EndOfMessage)
                                            break;
                                    } while(!cts.IsCancellationRequested && client.State == WebSocketState.Open);

                                    logger.Debug(() => $"Received WebSocket message with length {stream.Length}");

                                    // publish
                                    obs.OnNext(stream.ToArray());
                                }
                            }
                        }

                        catch (TaskCanceledException)
                        {
                            break;
                        }

                        catch (ObjectDisposedException)
                        {
                            break;
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

    private static IObservable<ZMessage> ZmqSubscribeEndpoint(ILogger logger, CancellationToken ct, string url, string topic)
    {
        return Observable.Defer(() => Observable.Create<ZMessage>(obs =>
        {
            var tcs = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var thread = new Thread(() =>
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

                        // do not run wild in case of a persistent error condition
                        Thread.Sleep(1000);
                    }
                }
            });

            thread.Start();

            return Disposable.Create(() =>
            {
                tcs.Cancel();
                tcs.Dispose();
            });
        }));
    }
}
