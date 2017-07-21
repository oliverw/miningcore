using System;
using System.Collections.Generic;
using System.Text;
using MiningForce.Configuration;

namespace MiningForce.Blockchain
{
	public class BaseShare : IShare
	{
		public string Worker { get; set; }
		public string IpAddress { get; set; }
		public DateTime Submitted { get; set; }
		public double Difficulty { get; set; }
		public double DifficultyNormalized { get; set; }
		public ulong BlockHeight { get; set; }
		public CoinType Coin { get; set; }
		public bool IsBlockCandidate { get; set; }
		public object BlockVerificationData { get; set; }
	}
}
