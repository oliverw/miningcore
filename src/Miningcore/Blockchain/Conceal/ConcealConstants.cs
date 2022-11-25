using System.Globalization;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Math;

namespace Miningcore.Blockchain.Conceal;

public enum ConcealNetworkType
{
    Main = 1,
    Test
}

public static class ConcealConstants
{
    public const string WalletDaemonCategory = "wallet";

    public const string DaemonRpcLocation = "json_rpc";
    public const string DaemonRpcGetInfoLocation = "getinfo";
    public const int ConcealRpcMethodNotFound = -32601;
    public const int PaymentIdHexLength = 64;
    public const decimal SmallestUnit = 1000000;
    public static readonly Regex RegexValidNonce = new("^[0-9a-f]{8}$", RegexOptions.Compiled);
    
    public static readonly BigInteger Diff1 = new("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", 16);
    public static readonly System.Numerics.BigInteger Diff1b = System.Numerics.BigInteger.Parse("00FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber);

    public const int PayoutMinBlockConfirmations = 10;

    public const int InstanceIdSize = 4;
    public const int ExtraNonceSize = 4;

    // NOTE: for whatever strange reason only reserved_size -1 can be used,
    // the LAST byte MUST be zero or nothing works
    public const int ReserveSize = ExtraNonceSize + InstanceIdSize + 1;

    // Offset to nonce in block blob
    public const int BlobNonceOffset = 39;

    public const decimal StaticTransactionFeeReserve = 0.001m; // in conceal
}

public static class ConcealCommands
{
    public const string GetInfo = "getinfo";
    public const string GetLastBlockHeader = "getlastblockheader";
    public const string GetBlockTemplate = "getblocktemplate";
    public const string SubmitBlock = "submitblock";
    public const string GetBlockHeaderByHash = "getblockheaderbyhash";
    public const string GetBlockHeaderByHeight = "getblockheaderbyheight";
}

public static class ConcealWalletCommands
{
    public const string GetBalance = "getBalance";
    public const string GetAddress = "getAddresses";
    public const string SendTransaction = "sendTransaction";
    public const string GetTransactions = "getTransactions";
    public const string SplitIntegratedAddress = "splitIntegrated";
    public const string Save = "save";
}