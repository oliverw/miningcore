using System;
using System.Collections.Generic;
using System.Text;
using MiningForce.Configuration;
using MiningForce.Stratum;
using MiningForce.VarDiff;

namespace MiningForce.MininigPool
{
    public class BanningStats
    {
        public int ValidShares { get; set; }
        public int InvalidShares { get; set; }
    }

    public class WorkerContext
    {
        public WorkerContext(StratumClient client, PoolConfig poolConfig)
        {
            Difficulty = client.PoolEndpoint.Difficulty;
	        LastActivity = DateTime.UtcNow;

			if (poolConfig.Banning != null)
                Stats = new BanningStats();

            if (client.PoolEndpoint.VarDiff != null)
                VarDiff = new VarDiffContext();
        }

        private double? pendingDifficulty;

        public BanningStats Stats { get; set; }
        public VarDiffContext VarDiff { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsAuthorized { get; set; } = false;
        public bool IsSubscribed { get; set; }
        public double Difficulty { get; set; }
        public double? PreviousDifficulty { get; set; }
	    public string UserAgent { get; set; }

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
