using System.Collections.Concurrent;

namespace Miningcore.Blockchain.Cryptonote;

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

    public readonly ConcurrentDictionary<string, bool> Submissions = new(StringComparer.OrdinalIgnoreCase);
}
