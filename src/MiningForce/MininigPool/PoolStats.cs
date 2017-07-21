using System;

namespace MiningForce.MininigPool
{
    public class PoolStats
    {
        public DateTime? LastBlockTime { get; set; }
        public int ConnectedMiners { get; set; }
		public float HashRate { get; set; }
        public float PoolFeePercent { get; set; }
        public float DonationsPercent { get; set; }

		// Telemetry
	    public float AverageResponseTimePerMinuteMs { get; set; }
	    public int SharesPerSecond { get; set; }
		public int ValidSharesPerMinute { get; set; }
	    public int InvalidSharesPerMinute { get; set; }
    }
}
