using System;
using System.Collections.Generic;
using System.Text;

namespace MiningForce.Blockchain.Bitcoin
{
    public static class BitcoinDaemonCommands
    {
	    public const string GetInfo = "getinfo";
	    public const string GetMiningInfo = "getmininginfo";
	    public const string GetPeerInfo = "getpeerinfo";
	    public const string ValidateAddress = "validateaddress";
	    public const string GetDifficulty = "getdifficulty";
	    public const string GetBlockTemplate = "getblocktemplate";
	    public const string SubmitBlock = "submitblock";
	    public const string GetBlockchainInfo = "getblockchaininfo";
	    public const string GetBlock = "getblock";
	    public const string GetTransaction = "gettransaction";
    }
}
