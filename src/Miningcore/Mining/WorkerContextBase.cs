using Miningcore.Configuration;
using Miningcore.Nicehash.API;
using Miningcore.Time;
using Miningcore.VarDiff;

namespace Miningcore.Mining;

public class ShareStats
{
    public int ValidShares { get; set; }
    public int InvalidShares { get; set; }
}

public class WorkerContextBase
{
    private double? pendingDifficulty;
    private string userAgent;

    public ShareStats Stats { get; set; }
    public VarDiffContext VarDiff { get; set; }
    public DateTime Created { get; set; }
    public DateTime LastActivity { get; set; }
    public bool IsAuthorized { get; set; }
    public bool IsSubscribed { get; set; }

    /// <summary>
    /// Difficulty assigned to this worker, either static or updated through VarDiffManager
    /// </summary>
    public double Difficulty { get; set; }

    /// <summary>
    /// Previous difficulty assigned to this worker
    /// </summary>
    public double? PreviousDifficulty { get; set; }

    /// <summary>
    /// UserAgent reported by Stratum
    /// </summary>
    public string UserAgent
    {
        get => userAgent;
        set
        {
            userAgent = value;

            IsNicehash = userAgent?.Contains(NicehashConstants.NicehashUA, StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    public bool IsNicehash { get; private set; }

    public void Init(double difficulty, VarDiffConfig varDiffConfig, IMasterClock clock)
    {
        Difficulty = difficulty;
        LastActivity = clock.Now;
        Created = clock.Now;
        Stats = new ShareStats();

        if(varDiffConfig != null)
        {
            VarDiff = new VarDiffContext
            {
                Created = Created,
                Config = varDiffConfig
            };
        }
    }

    public void EnqueueNewDifficulty(double difficulty)
    {
        pendingDifficulty = difficulty;
    }

    public bool HasPendingDifficulty => pendingDifficulty.HasValue;

    public bool ApplyPendingDifficulty()
    {
        if(pendingDifficulty.HasValue)
        {
            SetDifficulty(pendingDifficulty.Value);
            pendingDifficulty = null;

            return true;
        }

        return false;
    }

    public void SetDifficulty(double difficulty)
    {
        PreviousDifficulty = Difficulty;
        Difficulty = difficulty;
    }

}
