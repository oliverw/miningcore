using System;
using System.Collections.Generic;
using System.Text;

namespace MiningForce.Blockchain
{
	public class BaseShare : IShare
	{
		public string Worker { get; set; }
		public string IpAddress { get; set; }
		public DateTime Submitted { get; set; }
	}
}
