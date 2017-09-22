using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Persistence.Model
{
    public class MinerHashrateSample
    {
        public string PoolId { get; set; }
        public string Miner { get; set; }
        public double Hashrate { get; set; }
        public Dictionary<string, double> WorkerHashrates { get; set; }
        public DateTime Created { get; set; }
    }
}
