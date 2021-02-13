using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Miningcore.Blockchain.Cryptonote
{
    public class CryptonoteWorkerJob
    {
        public CryptonoteWorkerJob(string jobId, double difficulty)
        {
            Id = jobId;
            Difficulty = difficulty;
        }

        public string Id { get; }
        public uint Height { get; set; }
        public uint ExtraNonce { get; set; }
        public double Difficulty { get; set; }
        public string SeedHash { get; set; }

        public HashSet<string> Submissions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        //public readonly ConcurrentDictionary<string, bool> Submissions = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }
}
