using MiningForce.Blockchain;
using MiningForce.Configuration;
using MiningForce.Mining;

namespace MiningForce.RpcApi.ApiResponses
{
	public class PoolInfo
	{
		public string Id { get; set; }
		public PoolConfig Config { get; set; }
		public PoolStats PoolStats { get; set; }
		public BlockchainStats NetworkStats { get; set; }
	}

	public class GetPoolsResponse
    {
		public PoolInfo[] Pools { get; set; }
	}
}
