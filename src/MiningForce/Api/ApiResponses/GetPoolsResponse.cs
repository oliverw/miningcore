using System.Collections.Generic;
using MiningForce.Blockchain;
using MiningForce.Configuration;
using MiningForce.Mining;

namespace MiningForce.Api.ApiResponses
{
	public class PoolInfo
	{
		public string Id { get; set; }
		public bool Enabled { get; set; }
		public CoinConfig Coin { get; set; }
		public Dictionary<int, PoolEndpoint> Ports { get; set; }
		public PoolPaymentProcessingConfig PaymentProcessing { get; set; }
		public PoolBanningConfig Banning { get; set; }
		public RewardRecipient[] RewardRecipients { get; set; }
		public int ClientConnectionTimeout { get; set; }
		public int JobRebroadcastTimeout { get; set; }
		public int BlockRefreshInterval { get; set; }
		public PoolStats PoolStats { get; set; }
		public BlockchainStats NetworkStats { get; set; }
	}

	public class GetPoolsResponse
    {
		public PoolInfo[] Pools { get; set; }
	}
}
