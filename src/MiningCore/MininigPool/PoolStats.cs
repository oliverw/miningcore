using System;

namespace MiningCore.MininigPool
{
    public class PoolStats
    {
        public DateTime LastBlockTime { get; set; }
        public int ConnectedMiners { get; set; }
        public float HashRate { get; set; }
        public float PoolFeePercent { get; set; }
        public float DevDonationsPercent { get; set; }
    }
}
