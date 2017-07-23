using System;

namespace MiningForce.Persistence.Postgres.Entities
{
	public class Payment
	{
		public string PoolId { get; set; }
		public string Coin { get; set; }
		public long Blockheight { get; set; }
		public string Wallet { get; set; }
		public double Amount { get; set; }
		public DateTime Created { get; set; }
	}
}
