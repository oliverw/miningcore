using System;
using System.Numerics;
using System.Text.RegularExpressions;

namespace MiningCore.Blockchain.Ethereum
{
    public class EthereumConstants
    {
        public const ulong EpochLength = 30000;
        public const ulong CacheSizeForTesting = 1024;
        public const ulong DagSizeForTesting = 1024 * 32;
        public static BigInteger BigMaxValue = BigInteger.Pow(2, 256);
        public static double Pow2x32 = Math.Pow(2, 32);
        public static BigInteger BigPow2x32 = new BigInteger(Pow2x32);
        public const int AddressLength = 20;
        public const string EthereumStratumVersion = "EthereumStratum/1.0.0";
        public static readonly Regex ValidAddressPattern = new Regex("^0x[0-9a-fA-F]{40}$", RegexOptions.Compiled);
        public static readonly Regex ZeroHashPattern = new Regex("^0?x?0+$", RegexOptions.Compiled);
        public static readonly Regex NoncePattern = new Regex("^0x[0-9a-f]{16}$", RegexOptions.Compiled);
        public static readonly Regex HashPattern =  new Regex("^0x[0-9a-f]{64}$", RegexOptions.Compiled);
        public static readonly Regex WorkerPattern = new Regex("^[0-9a-zA-Z-_]{1,8}$", RegexOptions.Compiled);

        public const int InstanceIdSize = 3;
    }

    public enum EthereumNetworkType
    {
        Main = 1,
        Morden = 2,
        Ropsten = 3,
        Rinkeby = 4,
        Kovan = 42,

        Unknown = -1,
    }

    public static class GethCommands
    {
        public const string GetWork = "eth_getWork";
        public const string SubmitWork = "eth_submitWork";
        public const string Sign = "eth_sign";
        public const string GetNetVersion = "net_version";
        public const string GetAccounts = "eth_accounts";
        public const string GetPeerCount = "net_peerCount";
        public const string GetSyncState = "eth_syncing";
        public const string GetBlockByNumber = "eth_getBlockByNumber";
        public const string GetBlockByHash = "eth_getBlockByHash";
        public const string GetUncleByBlockNumberAndIndex = "eth_getUncleByBlockNumberAndIndex";
        public const string GetTxReceipt = "eth_getTransactionReceipt";
        public const string SendTx = "eth_sendTransaction";
    }
}
