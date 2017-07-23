using System;
using System.Collections.Generic;
using System.Text;
using MiningForce.Configuration;

namespace MiningForce.Blockchain
{
	public class CommonShare : IShare
	{
		public string PoolId { get; set; }
		public string Worker { get; set; }
		public string IpAddress { get; set; }
		public double Difficulty { get; set; }
		public double DifficultyNormalized { get; set; }
		public long BlockHeight { get; set; }
		public bool IsBlockCandidate { get; set; }
		public string TransactionConfirmationData { get; set; }
		public double NetworkDifficulty { get; set; }
	}
}
