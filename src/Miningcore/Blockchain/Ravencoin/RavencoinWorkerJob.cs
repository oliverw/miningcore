using System.Collections.Concurrent;
using System.Text;

namespace Miningcore.Blockchain.Ravencoin;

public class RavencoinWorkerJob
{
    public RavencoinWorkerJob(string jobId, string extraNonce1)
    {
        Id = jobId;
        ExtraNonce1 = extraNonce1;
    }

    public string Id { get; }
    public RavencoinJob Job { get; set; }
    public uint Height { get; set; }
    public string ExtraNonce1 { get; set; }
    public string Bits { get; set; }
    public string SeedHash { get; set; }

    public readonly ConcurrentDictionary<string, bool> Submissions = new(StringComparer.OrdinalIgnoreCase);

    public bool RegisterSubmit(string extraNonce1, string nonce, string headerHash, string mixHash)
    {
        var key = new StringBuilder()
            .Append(extraNonce1)
            .Append(nonce) // lowercase as we don't want to accept case-sensitive values as valid.
            .Append(headerHash)
            .Append(mixHash)
            .ToString();

        return Submissions.TryAdd(key, true);
    }
}