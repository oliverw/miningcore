using System.Numerics;

namespace MiningCore.Blockchain.Ethereum
{
    public class EthereumConstants
    {
        public const ulong EpochLength = 30000;
        public const ulong CacheSizeForTesting = 1024;
        public const ulong DagSizeForTesting = 1024 * 32;
        public static BigInteger BigMaxValue = BigInteger.Pow(2, 256);
        public const int AddressLength = 20;

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
