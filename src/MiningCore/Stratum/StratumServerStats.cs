using System;

namespace MiningCore.Stratum
{
    public class NetworkStats
    {
        public float HashRate { get; set; }
        public DateTime LastBlockTime { get; set; }
        public float Difficulty { get; set; }
        public int BlockHeight { get; set; }
    }

    public class PoolStats
    {
        public DateTime LastBlockTime { get; set; }
        public int ConnectedMiners { get; set; }
        public float HashRate { get; set; }
        public float PoolFeePercent { get; set; }
        public float DevDonationsPercent { get; set; }
    }
}
