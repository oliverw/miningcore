using System;
using System.Collections.Generic;
using System.Text;

namespace MiningForce.Blockchain.Bitcoin
{
    public class BitcoinShare : BaseShare
	{
	    public string BlockHex { get; set; }
	    public string BlockHash { get; set; }
	    public double BlockDiffAdjusted { get; set; }
	}
}
