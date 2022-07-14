namespace Miningcore.Pushover;

public static class PushoverConstants
{
    public const string ApiBaseUrl = "https://api.pushover.net/1";
}

public enum PushoverMessagePriority
{
    Silent = -2,
    Quiet = -1,
    None = 0,
    Priority = 1,
    PriorityWithConfirmation = 2
}
