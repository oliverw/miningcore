using System;
using MiningCore.Configuration;
using MiningCore.VarDiff;

namespace MiningCore.Mining
{
    public class BanningStats
    {
        public int ValidShares { get; set; }
        public int InvalidShares { get; set; }
    }

    public class WorkerContextBase
    {
        private double? pendingDifficulty;

        public BanningStats Stats { get; set; }
        public VarDiffContext VarDiff { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsAuthorized { get; set; } = false;
        public bool IsSubscribed { get; set; }
        public double Difficulty { get; set; }
        public double? PreviousDifficulty { get; set; }
        public string UserAgent { get; set; }

        public bool HasPendingDifficulty => pendingDifficulty.HasValue;

        public void Init(PoolConfig poolConfig, double difficulty, VarDiffConfig varDiffConfig)
        {
            Difficulty = difficulty;
            LastActivity = DateTime.UtcNow;

            if (poolConfig.Banning != null)
                Stats = new BanningStats();

            if (varDiffConfig != null)
                VarDiff = new VarDiffContext();
        }

        public void EnqueueNewDifficulty(double difficulty)
        {
            pendingDifficulty = difficulty;
        }

        public bool ApplyPendingDifficulty()
        {
            if (pendingDifficulty.HasValue)
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
}
