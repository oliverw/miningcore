/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using MiningCore.Configuration;
using MiningCore.Time;
using MiningCore.VarDiff;

namespace MiningCore.Mining
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

            if (varDiffConfig != null)
                VarDiff = new VarDiffContext { Config = varDiffConfig };
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

        public void Dispose()
        {
        }
    }
}
