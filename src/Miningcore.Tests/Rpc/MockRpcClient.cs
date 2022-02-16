using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Miningcore.Blockchain.Ethereum;
using Miningcore.Configuration;
using Miningcore.Rpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using ZeroMQ;

namespace Miningcore.Tests.Rpc
{
    public class MockRpcClient : IRpcClient
    {
        public Task<RpcResponse<TResponse>> ExecuteAsync<TResponse>(ILogger logger, string method, CancellationToken ct, object payload = null, bool throwOnError = false)
        where TResponse : class
        {
            return Task.FromResult(method switch
            {
                EthCommands.GetPeerCount => new RpcResponse<TResponse>((TResponse) ($"0x{5:X}" as object)),
                _ => throw new NotImplementedException()
            }
            );
        }

        public Task<RpcResponse<JToken>> ExecuteAsync(ILogger logger, string method, CancellationToken ct, bool throwOnError = false)
        {
            throw new NotImplementedException();
        }

        public Task<RpcResponse<JToken>[]> ExecuteBatchAsync(ILogger logger, CancellationToken ct, params RpcRequest[] batch)
        {
            throw new NotImplementedException();
        }

        public IObservable<byte[]> WebsocketSubscribe(ILogger logger, CancellationToken ct, DaemonEndpointConfig endPoint,
            string method, object payload = null,
            JsonSerializerSettings payloadJsonSerializerSettings = null)
        {
            throw new NotImplementedException();
        }

        public IObservable<ZMessage> ZmqSubscribe(ILogger logger, CancellationToken ct, Dictionary<DaemonEndpointConfig, (string Socket, string Topic)> portMap)
        {
            throw new NotImplementedException();
        }
    }
}
