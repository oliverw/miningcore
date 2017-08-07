using System;

namespace MiningCore.Mining
{
	public class PoolStats
	{
        public DateTime? LastPoolBlockTime { get; set; }
        public int ConnectedMiners { get; set; }
		public float PoolHashRate { get; set; }
        public float PoolFeePercent { get; set; }
        public float DonationsPercent { get; set; }

		// Telemetry
	    public int SharesPerSecond { get; set; }
		public int ValidSharesPerMinute { get; set; }
	    public int InvalidSharesPerMinute { get; set; }
    }
}
