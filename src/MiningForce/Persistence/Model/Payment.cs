using System;

namespace MiningForce.Persistence.Model
{
	public class Payment
	{
		public string Coin { get; set; }
		public long Blockheight { get; set; }
		public string Wallet { get; set; }
		public double Amount { get; set; }
		public DateTime Created { get; set; }
	}
}
