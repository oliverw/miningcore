using System.Globalization;
using System.Text.RegularExpressions;
using NBitcoin.BouncyCastle.Math;

namespace MiningCore.Blockchain.Monero
{
    public enum MoneroNetworkType
    {
        Main = 1,
        Test
    }

    public class MoneroConstants
    {
        public const string WalletDaemonCategory = "wallet";
        public const int AddressLength = 95;
        public const string DaemonRpcLocation = "json_rpc";
        public const string DaemonRpcDigestAuthRealm = "monero_rpc";
        public const char MainNetAddressPrefix = '4';
        public const char TestNetAddressPrefix = '9';
        public static readonly Regex RegexValidNonce = new Regex("^[0-9a-f]{8}$", RegexOptions.Compiled);

        public static readonly BigInteger Diff1 = new BigInteger("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", 16);
        public static readonly System.Numerics.BigInteger Diff1b = System.Numerics.BigInteger.Parse("0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber);

        public const double DifficultyNormalizationFactor = 14226363340d;

#if !DEBUG
		public const int PayoutMinBlockConfirmations = 60;
#else
        public const int PayoutMinBlockConfirmations = 2;
#endif

        public const int InstanceIdSize = 3;
        public const int ExtraNonceSize = 4;

        // NOTE: for whatever strange reason only reserved_size -1 can be used,
        // the LAST byte MUST be zero or nothing works
        public const int ReserveSize = ExtraNonceSize + InstanceIdSize + 1;

        // Offset to nonce in block blob
        public const int BlobNonceOffset = 39;

        public const long Piconero = (long) 1e12;
        public const decimal StaticTransactionFeeReserve = 0.01m; // in monero
        public const decimal DevReward = 0.002m;

        public const string DevAddress = "475YVJbPHPedudkhrcNp1wDcLMTGYusGPF5fqE7XjnragVLPdqbCHBdZg3dF4dN9hXMjjvGbykS6a77dTAQvGrpiQqHp2eH";
    }

    public static class MoneroCommands
    {
        public const string GetInfo = "get_info";
        public const string GetBlockTemplate = "getblocktemplate";
        public const string SubmitBlock = "submitblock";
        public const string GetBlockHeaderByHash = "getblockheaderbyhash";
        public const string GetBlockHeaderByHeight = "getblockheaderbyheight";
    }

    public static class MoneroWalletCommands
    {
        public const string GetBalance = "getbalance";
        public const string GetAddress = "getaddress";
        public const string Transfer = "transfer";
        public const string TransferSplit = "transfer_split";
        public const string GetTransfers = "get_transfers";
        public const string SplitIntegratedAddress = "split_integrated_address";
    }
}
