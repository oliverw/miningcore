using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Notifications.Messages
{
    public enum TelemetryCategory
    {
        Share = 1,  // Share processed
        BtStream,   // Blocktemplate over BTStream
    }

    public class TelemetryEvent
    {
        public TelemetryEvent(string server, string poolId, TelemetryCategory category, TimeSpan elapsed, bool? success = null)
        {
            Server = server;
            PoolId = poolId;
            Category = category;
            Elapsed = elapsed;
            Success = success;
        }

        public string Server { get; set; }
        public string PoolId { get; set; }
        public TelemetryCategory Category { get; set; }
        public TimeSpan Elapsed { get; set; }
        public bool? Success { get; set; }
    }
}
