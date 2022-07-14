namespace Miningcore.Blockchain;

public static class JobRefreshBy
{
    public const string Initial = "INIT";
    public const string Poll = "POLL";
    public const string PollRefresh = "POLL-R";
    public const string PubSub = "ZMQ";
    public const string BlockTemplateStream = "BTS";
    public const string BlockTemplateStreamRefresh = "BTS-R";
    public const string WebSocket = "WS";
    public const string BlockFound = "BLOCK";
}
