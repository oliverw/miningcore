using System.Globalization;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Math;

namespace Miningcore.Blockchain.Cryptonote;

public enum CryptonoteNetworkType
{
    Main = 1,
    Test,
    Stage
}

public static class CryptonoteConstants
{
    public const string WalletDaemonCategory = "wallet";

    public const string DaemonRpcLocation = "json_rpc";
    public const int MoneroRpcMethodNotFound = -32601;
    public const int PaymentIdHexLength = 64;
    public static readonly Regex RegexValidNonce = new("^[0-9a-f]{8}$", RegexOptions.Compiled);

    public static readonly BigInteger Diff1 = new("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", 16);
    public static readonly System.Numerics.BigInteger Diff1b = System.Numerics.BigInteger.Parse("00FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber);

#if DEBUG
    public const int PayoutMinBlockConfirmations = 2;
#else
        public const int PayoutMinBlockConfirmations = 60;
#endif

    public const int InstanceIdSize = 4;
    public const int ExtraNonceSize = 4;

    // NOTE: for whatever strange reason only reserved_size -1 can be used,
    // the LAST byte MUST be zero or nothing works
    public const int ReserveSize = ExtraNonceSize + InstanceIdSize + 1;

    // Offset to nonce in block blob
    public const int BlobNonceOffset = 39;

    public const decimal StaticTransactionFeeReserve = 0.03m; // in monero
}

public static class CryptonoteCommands
{
    public const string GetInfo = "get_info";
    public const string GetBlockTemplate = "getblocktemplate";
    public const string SubmitBlock = "submitblock";
    public const string GetBlockHeaderByHash = "getblockheaderbyhash";
    public const string GetBlockHeaderByHeight = "getblockheaderbyheight";
}

public static class CryptonoteWalletCommands
{
    public const string GetBalance = "get_balance";
    public const string GetAddress = "getaddress";
    public const string Transfer = "transfer";
    public const string TransferSplit = "transfer_split";
    public const string GetTransfers = "get_transfers";
    public const string SplitIntegratedAddress = "split_integrated_address";
    public const string Store = "store";
}
