namespace Miningcore.JsonRpc;

public record RpcResponse<T>(T Response, JsonRpcError Error = null);
