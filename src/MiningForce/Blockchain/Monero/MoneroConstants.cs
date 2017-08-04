using MiningForce.Extensions;
using NBitcoin.BouncyCastle.Math;

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

		public const int InstanceIdSize = 3;
		public const int ExtraNonceSize = 4;

		// NOTE: for whatever strange reason only reserved_size -1 can be used,
		// the LAST byte MUST be zero or nothing works
		public const int ReserveSize = ExtraNonceSize + InstanceIdSize + 1;

		public const int AddressLength = 95;

		public const string DaemonRpcLocation = "json_rpc";
		public const string DaemonRpcDigestAuthRealm = "monero_rpc";

		public const char MainNetAddressPrefix = '4';
		public const char TestNetAddressPrefix = '9';

		public static readonly BigInteger Diff1 = new BigInteger("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", 16);
	}
}
