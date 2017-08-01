using MiningForce.Configuration;
using MiningForce.Mining;
using MiningForce.Stratum;

namespace MiningForce.Blockchain.Monero
{
    public class MoneroWorkerContext : WorkerContextBase
	{
		public string ExtraNonce1 { get; set; }
	}
}
