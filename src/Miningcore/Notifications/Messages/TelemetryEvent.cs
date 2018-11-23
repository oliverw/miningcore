using System;
using System.Collections.Generic;
using System.Text;

namespace Miningcore.Notifications.Messages
{
    public enum TelemetryCategory
    {
        Share = 1, // Share processed
        BtStream, // Blocktemplate over BTStream
        RpcRequest // JsonRPC Request to Daemon
    }

    public class TelemetryEvent
    {
        public TelemetryEvent(string server, string poolId, TelemetryCategory category, TimeSpan elapsed, bool? success = null, string error = null)
        {
            Server = server;
            PoolId = poolId;
            Category = category;
            Elapsed = elapsed;
            Success = success;
            Error = error;
        }

        public TelemetryEvent(string server, string poolId, TelemetryCategory category, string info, TimeSpan elapsed, bool? success = null, string error = null) :
            this(server, poolId, category, elapsed, success, error)
        {
            Info = info;
        }

        public string Server { get; set; }
        public string PoolId { get; set; }
        public TelemetryCategory Category { get; set; }
        public string Info { get; }
        public TimeSpan Elapsed { get; set; }
        public bool? Success { get; set; }
        public string Error { get; }
    }
}
