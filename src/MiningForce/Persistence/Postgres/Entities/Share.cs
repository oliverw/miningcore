using System;

namespace MiningForce.Persistence.Postgres.Entities
{
	public class Share
	{
		public long Id { get; set; }
		public string PoolId { get; set; }
		public long Blockheight { get; set; }
		public string Worker { get; set; }
		public double Difficulty { get; set; }
		public double NetworkDifficulty { get; set; }
		public string IpAddress { get; set; }
		public DateTime Created { get; set; }
	}
}
