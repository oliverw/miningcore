using System;

namespace MiningForce.MininigPool
{
	/// <summary>
	/// By encapsulating this in an interface we can enforce a contract
	/// in other layers consuming these metrics (such as persistence)
	/// </summary>
	public interface IPoolStats
	{
		 DateTime? LastPoolBlockTime { get; set; }
		 int ConnectedMiners { get; set; }
		 float PoolHashRate { get; set; }
		 float PoolFeePercent { get; set; }
		 float DonationsPercent { get; set; }

		// Telemetry
		 float AverageResponseTimePerMinuteMs { get; set; }
		 int SharesPerSecond { get; set; }
		 int ValidSharesPerMinute { get; set; }
		 int InvalidSharesPerMinute { get; set; }
	}

	public class PoolStats : IPoolStats
	{
        public DateTime? LastPoolBlockTime { get; set; }
        public int ConnectedMiners { get; set; }
		public float PoolHashRate { get; set; }
        public float PoolFeePercent { get; set; }
        public float DonationsPercent { get; set; }

		// Telemetry
	    public float AverageResponseTimePerMinuteMs { get; set; }
	    public int SharesPerSecond { get; set; }
		public int ValidSharesPerMinute { get; set; }
	    public int InvalidSharesPerMinute { get; set; }
    }
}
