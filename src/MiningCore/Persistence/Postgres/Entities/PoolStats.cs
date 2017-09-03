using System;

namespace MiningCore.Persistence.Postgres.Entities
{
    public class PoolStats
    {
        public long Id { get; set; }
        public string PoolId { get; set; }

        public int ConnectedMiners { get; set; }
        public float PoolHashRate { get; set; }

        // Telemetry
        public int SharesPerSecond { get; set; }
        public int ValidSharesPerMinute { get; set; }
        public int InvalidSharesPerMinute { get; set; }

        public DateTime Created { get; set; }
    }
}
