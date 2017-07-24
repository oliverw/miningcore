using System;
using System.Collections.Generic;
using System.Text;
using MiningForce.Configuration;
using MiningForce.JsonRpc;

namespace MiningForce.Blockchain.Daemon
{
	public class DaemonResponse<T>
	{
		public JsonRpcException Error { get; set; }
		public T Response { get; set; }
		public AuthenticatedNetworkEndpointConfig Instance { get; set; }
	}
}
