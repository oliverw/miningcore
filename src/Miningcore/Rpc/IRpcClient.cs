using Miningcore.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using ZeroMQ;

namespace Miningcore.Rpc;

/// <summary>
/// JsonRpc interface to blockchain node
/// </summary>
public interface IRpcClient
{
    #region API-Surface

    Task<RpcResponse<TResponse>> ExecuteAsync<TResponse>(ILogger logger, string method, CancellationToken ct,
        object payload = null, bool throwOnError = false)
        where TResponse : class;

    Task<RpcResponse<JToken>> ExecuteAsync(ILogger logger, string method, CancellationToken ct, bool throwOnError = false);

    Task<RpcResponse<JToken>[]> ExecuteBatchAsync(ILogger logger, CancellationToken ct, params RpcRequest[] batch);

    IObservable<byte[]> WebsocketSubscribe(ILogger logger, CancellationToken ct, DaemonEndpointConfig endPoint,
        string method, object payload = null,
        JsonSerializerSettings payloadJsonSerializerSettings = null);

    IObservable<ZMessage> ZmqSubscribe(ILogger logger, CancellationToken ct, Dictionary<DaemonEndpointConfig, (string Socket, string Topic)> portMap);

    #endregion // API-Surface
}