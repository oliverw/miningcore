using System;
using MiningForce.Blockchain;
using MiningForce.Mining;

namespace MiningForce.Persistence.Postgres.Entities
{
	public class PoolAndBlockchainStats :
		IPoolStats,
		IBlockchainStats
	{
		public string PoolId { get; set; }

		#region Implementation of IPoolStats

		public DateTime? LastPoolBlockTime { get; set; }
		public int ConnectedMiners { get; set; }
		public float PoolHashRate { get; set; }
		public float PoolFeePercent { get; set; }
		public float DonationsPercent { get; set; }
		public float AverageResponseTimePerMinuteMs { get; set; }
		public int SharesPerSecond { get; set; }
		public int ValidSharesPerMinute { get; set; }
		public int InvalidSharesPerMinute { get; set; }

		#endregion

		#region Implementation of IBlockchainStats

		public string NetworkType { get; set; }
		public double NetworkHashRate { get; set; }
		public DateTime? LastNetworkBlockTime { get; set; }
		public double NetworkDifficulty { get; set; }
		public int BlockHeight { get; set; }
		public int ConnectedPeers { get; set; }
		public string RewardType { get; set; }

		#endregion
	}
}
