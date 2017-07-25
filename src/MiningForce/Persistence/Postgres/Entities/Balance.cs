using System;

namespace MiningForce.Persistence.Postgres.Entities
{
	public class Balance
	{
		public string PoolId { get; set; }
		public string Coin { get; set; }
		public string Address { get; set; }
		public double Amount { get; set; }
		public DateTime Created { get; set; }
		public DateTime Updated { get; set; }
	}
}
