using MiningForce.Configuration;
using MiningForce.JsonRpc;

namespace MiningForce.DaemonInterface
{
	public class DaemonResponse<T>
	{
		public JsonRpcException Error { get; set; }
		public T Response { get; set; }
		public AuthenticatedNetworkEndpointConfig Instance { get; set; }
	}
}
