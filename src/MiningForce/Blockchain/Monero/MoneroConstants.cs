namespace MiningForce.Blockchain.Monero
{
	public enum MoneroNetworkType
	{
		Main = 1,
		Test,
	}

	public class MoneroConstants
	{
		public const string WalletDaemonCategory = "wallet";
		public const int AddressLength = 95;

		public const string DaemonRpcLocation = "json_rpc";
		public const string DaemonRpcDigestAuthRealm = "monero_rpc";

		public const char MainNetAddressPrefix = '4';
		public const char TestNetAddressPrefix = '9';
	}
}
