using System;
using MiningForce.Configuration;

namespace MiningForce.Persistence.Model
{
	public class Payment
	{
		public long Id { get; set; }
		public string PoolId { get; set; }
		public CoinType Coin { get; set; }
		public long Blockheight { get; set; }
		public string Wallet { get; set; }
		public double Amount { get; set; }
		public DateTime Created { get; set; }
	}
}
