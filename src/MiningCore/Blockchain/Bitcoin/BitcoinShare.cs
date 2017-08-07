using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;

namespace MiningCore.Blockchain.Bitcoin
{
    public class BitcoinShare : ShareBase
	{
	    public string BlockHex { get; set; }
	    public string BlockHash { get; set; }
	}
}
