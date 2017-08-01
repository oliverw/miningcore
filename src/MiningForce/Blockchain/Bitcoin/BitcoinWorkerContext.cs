using System;
using System.Collections.Generic;
using System.Text;
using MiningForce.Configuration;
using MiningForce.Mining;
using MiningForce.Stratum;

namespace MiningForce.Blockchain.Bitcoin
{
    public class BitcoinWorkerContext : WorkerContextBase
	{
		public string ExtraNonce1 { get; set; }
	}
}
