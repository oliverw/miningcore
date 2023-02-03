using Miningcore.Mining;

namespace Miningcore.Blockchain.Ravencoin;

public class RavencoinWorkerContext : WorkerContextBase
{
    /// <summary>
    /// Usually a wallet address
    /// </summary>
    public string Miner { get; set; }

    /// <summary>
    /// Arbitrary worker identififer for miners using multiple rigs
    /// </summary>
    public string Worker { get; set; }

    /// <summary>
    /// Unique value assigned per worker
    /// </summary>
    public string ExtraNonce1 { get; set; }

    private List<RavencoinWorkerJob> validJobs { get; } = new();

    public void AddJob(RavencoinWorkerJob job)
    {
        validJobs.Insert(0, job);

        while(validJobs.Count > 4)
            validJobs.RemoveAt(validJobs.Count - 1);
    }

    public RavencoinWorkerJob FindJob(string jobId)
    {
        return validJobs.FirstOrDefault(x => x.Id == jobId);
    }
}
