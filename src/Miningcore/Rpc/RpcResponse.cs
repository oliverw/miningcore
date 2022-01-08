using Miningcore.JsonRpc;

namespace Miningcore.Rpc;

public record RpcResponse<T>(T Response, JsonRpcError Error = null);
