using Miningcore.Mining;

namespace Miningcore.Blockchain.Conceal;

public class ConcealWorkerContext : WorkerContextBase
{
    /// <summary>
    /// Usually a wallet address
    /// NOTE: May include paymentid (seperated by a dot .)
    /// </summary>
    public string Miner { get; set; }

    /// <summary>
    /// Arbitrary worker identififer for miners using multiple rigs
    /// </summary>
    public string Worker { get; set; }

    private List<ConcealWorkerJob> validJobs { get; } = new();

    public void AddJob(ConcealWorkerJob job)
    {
        validJobs.Insert(0, job);

        while(validJobs.Count > 4)
            validJobs.RemoveAt(validJobs.Count - 1);
    }

    public ConcealWorkerJob FindJob(string jobId)
    {
        return validJobs.FirstOrDefault(x => x.Id == jobId);
    }
}