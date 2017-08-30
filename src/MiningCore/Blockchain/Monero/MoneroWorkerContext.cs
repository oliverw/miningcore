using System.Collections.Generic;
using MiningCore.Mining;

namespace MiningCore.Blockchain.Monero
{
    public class MoneroWorkerContext : WorkerContextBase
    {
        public string MinerName { get; set; }
        public string WorkerName { get; set; }
        public string PaymentId { get; set; }
        public List<MoneroWorkerJob> ValidJobs { get; } = new List<MoneroWorkerJob>();

        public void AddJob(MoneroWorkerJob job)
        {
            ValidJobs.Add(job);

            while (ValidJobs.Count > 4)
                ValidJobs.RemoveAt(0);
        }
    }
}
