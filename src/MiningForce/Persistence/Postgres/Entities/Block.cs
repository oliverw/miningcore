using System;

namespace MiningForce.Persistence.Postgres.Entities
{
	public class Block
	{
		public string PoolId { get; set; }
		public long Blockheight { get; set; }
		public string Status { get; set; }
		public string TransactionConfirmationData { get; set; }
		public DateTime Created { get; set; }
	}
}
