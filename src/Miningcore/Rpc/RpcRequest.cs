namespace Miningcore.Rpc;

public record RpcRequest
{
    public RpcRequest(string method)
    {
        Method = method;
    }

    public RpcRequest(string method, object payload)
    {
        Method = method;
        Payload = payload;
    }

    public string Method { get; }
    public object Payload { get; }
}
