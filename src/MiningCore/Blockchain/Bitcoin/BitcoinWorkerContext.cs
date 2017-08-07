using System;
using System.Collections.Generic;
using System.Text;
using MiningCore.Configuration;
using MiningCore.Mining;
using MiningCore.Stratum;

namespace MiningCore.Blockchain.Bitcoin
{
    public class BitcoinWorkerContext : WorkerContextBase
	{
		public string ExtraNonce1 { get; set; }
	}
}
