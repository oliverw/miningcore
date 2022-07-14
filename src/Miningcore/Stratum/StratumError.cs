namespace Miningcore.Stratum;

public enum StratumError
{
    Other = 20,
    JobNotFound = 21, // stale
    DuplicateShare = 22,
    LowDifficultyShare = 23,
    UnauthorizedWorker = 24,
    NotSubscribed = 25,
    MinusOne = -1
}

public class StratumException : Exception
{
    public StratumException(StratumError code, string message) : base(message)
    {
        Code = code;
    }

    public StratumError Code { get; set; }
}
