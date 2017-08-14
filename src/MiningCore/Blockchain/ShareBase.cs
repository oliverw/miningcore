using System;
using System.Collections.Generic;
using System.Text;
using MiningCore.Configuration;

namespace MiningCore.Blockchain
{
    public class ShareBase : IShare
    {
        public string PoolId { get; set; }
        public string Miner { get; set; }
        public string Worker { get; set; }
        public string IpAddress { get; set; }
        public double Difficulty { get; set; }
        public double StratumDifficulty { get; set; }
        public double StratumDifficultyBase { get; set; }
        public double NetworkDifficulty { get; set; }
        public double NormalizedDifficulty { get; set; }
        public long BlockHeight { get; set; }
        public decimal BlockReward { get; set; }
        public bool IsBlockCandidate { get; set; }
        public string TransactionConfirmationData { get; set; }
        public DateTime Created { get; set; }
    }
}