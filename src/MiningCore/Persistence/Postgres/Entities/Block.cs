using System;

namespace MiningCore.Persistence.Postgres.Entities
{
	public class Block
	{
		public long Id { get; set; }
		public string PoolId { get; set; }
		public long Blockheight { get; set; }
		public string Status { get; set; }
		public string TransactionConfirmationData { get; set; }
		public decimal Reward { get; set; }
		public DateTime Created { get; set; }
	}
}
