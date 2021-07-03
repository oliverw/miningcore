using System;
using System.Collections.Generic;
using System.Text;

namespace Miningcore.Notifications.Messages
{
    public enum TelemetryCategory
    {
        /// <summary>
        /// Share processed
        /// </summary>
        Share = 1,

        /// <summary>
        /// Blocktemplate over BTStream received
        /// </summary>
        BtStream,

        /// <summary>
        /// JsonRPC Request to Daemon
        /// </summary>
        RpcRequest,

        /// <summary>
        /// Number of TCP connections to a pool
        /// </summary>
        Connections,
    }

    public class TelemetryEvent
    {
        public TelemetryEvent(string server, string poolId, TelemetryCategory category, TimeSpan elapsed, bool? success = null, string error = null, int? total = null)
        {
            Server = server;
            PoolId = poolId;
            Category = category;
            Elapsed = elapsed;
            Success = success;
            Error = error;

            if(total.HasValue)
                Total = total.Value;
        }

        public TelemetryEvent(string server, string poolId, TelemetryCategory category, string info, TimeSpan elapsed, bool? success = null, string error = null, int? total = null) :
            this(server, poolId, category, elapsed, success, error, total)
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
        public int Total { get; set; }
    }
}
