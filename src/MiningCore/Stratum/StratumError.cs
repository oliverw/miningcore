using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Stratum
{
    public enum StratumError
    {
        Other = 20,
        JobNotFound = 21, // stale
        DuplicateShare = 22,
        LowDifficultyShare = 23,
        UnauthorizedWorker = 24,
        NotSubscribed = 25,
    }
}
