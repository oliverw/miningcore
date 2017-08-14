using System;

namespace MiningCore.Persistence.Model
{
    public class Share
    {
        public long Id { get; set; }
        public string PoolId { get; set; }
        public ulong Blockheight { get; set; }
        public string Miner { get; set; }
        public string Worker { get; set; }
        public double Difficulty { get; set; }
        public double StratumDifficulty { get; set; }
        public double StratumDifficultyBase { get; set; }
        public double NetworkDifficulty { get; set; }
        public string IpAddress { get; set; }
        public DateTime Created { get; set; }
    }
}
