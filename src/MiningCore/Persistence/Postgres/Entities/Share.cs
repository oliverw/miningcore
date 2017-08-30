using System;

namespace MiningCore.Persistence.Postgres.Entities
{
    public class Share
    {
        public long Id { get; set; }
        public string PoolId { get; set; }
        public long Blockheight { get; set; }
        public string PayoutInfo { get; set; }
        public string Miner { get; set; }
        public string Worker { get; set; }
        public string UserAgent { get; set; }
        public double Difficulty { get; set; }
        public double StratumDifficulty { get; set; }
        public double StratumDifficultyBase { get; set; }
        public double NetworkDifficulty { get; set; }
        public string IpAddress { get; set; }
        public DateTime Created { get; set; }
    }
}
