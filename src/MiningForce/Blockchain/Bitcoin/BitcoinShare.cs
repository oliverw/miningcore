using System;
using System.Collections.Generic;
using System.Text;

namespace MiningForce.Blockchain.Bitcoin
{
    public class BitcoinShare : BaseShare
	{
	    public bool IsBlockCandidate { get; set; }
		public bool IsAccepted { get; set; }
		public string JobId { get; set; }
		public uint Height { get; set; }
		public long BlockReward { get; set; }   // satoshis
		public double Difficulty { get; set; }
	    public string BlockHex { get; set; }
	    public string BlockHash { get; set; }
	    public double BlockDiffAdjusted { get; set; }
	}
}
