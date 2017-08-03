using System.Collections.Generic;
using MiningForce.Mining;

namespace MiningForce.Blockchain.Monero
{
    public class MoneroWorkerContext : WorkerContextBase
	{
		public uint LastQueryBlockHeight { get; set; }
		public List<MoneroWorkerJob> ValidJobs { get; } = new List<MoneroWorkerJob>();

		public void AddJob(MoneroWorkerJob job)
		{
			ValidJobs.Add(job);

			while (ValidJobs.Count > 4)
				ValidJobs.RemoveAt(0);
		}
	}
}
