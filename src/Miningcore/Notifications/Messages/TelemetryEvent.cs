namespace Miningcore.Notifications.Messages;

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

    /// <summary>
    /// Hash computation time
    /// </summary>
    Hash,

    /// <summary>
    /// JsonRPC Request from miner
    /// </summary>
    StratumRequest,

    /// <summary>
    /// API request handled
    /// </summary>
    ApiRequest
}

public record TelemetryEvent(string GroupId, TelemetryCategory Category, TimeSpan Elapsed, bool? Success = null, string Error = null)
{
    public TelemetryEvent(string groupId, TelemetryCategory category, string info, TimeSpan elapsed, bool? success = null, string error = null) :
        this(groupId, category, elapsed, success, error)
    {
        Info = info;
    }

    public string Info { get; }
    public int Total { get; set; }
}
