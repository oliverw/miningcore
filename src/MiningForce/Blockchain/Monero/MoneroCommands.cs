namespace MiningForce.Blockchain.Monero
{
    public static class MoneroCommands
    {
	    public const string GetInfo = "get_info";
	    public const string GetBlockTemplate = "getblocktemplate";
	    public const string SubmitBlock = "submitblock";
    }

	public static class MoneroWalletCommands
	{
		public const string GetBalance = "getbalance";
		public const string GetAddress= "getaddress";
		public const string Transfer = "transfer";
		public const string TransferSplit = "transfer_split";
		public const string GetTransfers = "get_transfers";
		public const string SplitIntegratedAddress = "split_integrated_address";
	}
}
