using Miningcore.Configuration;
using Miningcore.JsonRpc;

namespace Miningcore.DaemonInterface
{
    public class DaemonResponse<T>
    {
        public JsonRpcException Error { get; set; }
        public T Response { get; set; }
        public AuthenticatedNetworkEndpointConfig Instance { get; set; }
    }
}
