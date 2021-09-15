using System;
using Miningcore.Configuration;
using Miningcore.Time;
using Miningcore.VarDiff;

namespace Miningcore.Mining
{
    public class ShareStats
    {
        public int ValidShares { get; set; }
        public int InvalidShares { get; set; }
    }

    public class WorkerContextBase
    {
        private double? pendingDifficulty;

        public ShareStats Stats { get; set; }
        public VarDiffContext VarDiff { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsAuthorized { get; set; } = false;
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
        public string UserAgent { get; set; }

        /// <summary>
        /// True if there's a difficulty update queued for this worker
        /// </summary>
        public bool HasPendingDifficulty => pendingDifficulty.HasValue;

        public void Init(PoolConfig poolConfig, double difficulty, VarDiffConfig varDiffConfig, IMasterClock clock)
        {
            Difficulty = difficulty;
            LastActivity = clock.Now;
            Stats = new ShareStats();

            if(varDiffConfig != null)
                VarDiff = new VarDiffContext { Config = varDiffConfig };
        }

        public void EnqueueNewDifficulty(double difficulty)
        {
            pendingDifficulty = difficulty;
        }

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

        public void Dispose()
        {
        }
    }
}
