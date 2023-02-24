using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Miningcore.Stratum;
using NLog;
using Contract = Miningcore.Contracts.Contract;

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

    private readonly ConcurrentDictionary<string, bool> submissions = new(StringComparer.OrdinalIgnoreCase);

    private bool RegisterSubmit(string nonce, string headerHash, string mixHash)
    {
        var key = new StringBuilder()
            .Append(nonce) // lowercase as we don't want to accept case-sensitive values as valid.
            .Append(headerHash)
            .Append(mixHash)
            .ToString();

        return submissions.TryAdd(key, true);
    }

    public (Share Share, string BlockHex) ProcessShare(ILogger logger, StratumConnection worker, string nonce, string headerHash, string mixHash)
    {
        Contract.RequiresNonNull(worker);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce));

        var context = worker.ContextAs<RavencoinWorkerContext>();

        // mixHash
        if(mixHash.Length != 64)
            throw new StratumException(StratumError.Other, $"incorrect size of mixHash: {mixHash}");

        // validate nonce
        if(nonce.Length != 16)
            throw new StratumException(StratumError.Other, $"incorrect size of nonce: {nonce}");

        // check if nonce is within range
        if(nonce.IndexOf(context.ExtraNonce1[0..4], StringComparison.OrdinalIgnoreCase) != 0)
            throw new StratumException(StratumError.Other, $"nonce out of range: {nonce}");

        // dupe check
        if(!RegisterSubmit(nonce, headerHash, mixHash))
            throw new StratumException(StratumError.DuplicateShare, "duplicate share");

        var nonceLong = ulong.Parse(nonce, NumberStyles.HexNumber);

        return Job.ProcessShareInternal(logger, worker, nonceLong, headerHash, mixHash);
    }
}
