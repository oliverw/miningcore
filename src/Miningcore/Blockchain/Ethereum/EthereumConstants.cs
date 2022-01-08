using System.Numerics;
using System.Text.RegularExpressions;

namespace Miningcore.Blockchain.Ethereum;

public class EthereumConstants
{
    public const ulong EpochLength = 30000;
    public const ulong CacheSizeForTesting = 1024;
    public const ulong DagSizeForTesting = 1024 * 32;
    public static BigInteger BigMaxValue = BigInteger.Pow(2, 256);
    public static double Pow2x32 = Math.Pow(2, 32);
    public static BigInteger BigPow2x32 = new(Pow2x32);
    public const int AddressLength = 20;
    public const decimal Wei = 1000000000000000000;
    public static BigInteger WeiBig = new(1000000000000000000);
    public const string EthereumStratumVersion = "EthereumStratum/1.0.0";
    public const decimal StaticTransactionFeeReserve = 0.0025m; // in ETH
    public const string BlockTypeUncle = "uncle";

#if !DEBUG
        public const int MinPayoutPeerCount = 1;
#else
    public const int MinPayoutPeerCount = 1;
#endif

    public static readonly Regex ValidAddressPattern = new("^0x[0-9a-fA-F]{40}$", RegexOptions.Compiled);
    public static readonly Regex ZeroHashPattern = new("^0?x?0+$", RegexOptions.Compiled);
    public static readonly Regex NoncePattern = new("^0x[0-9a-f]{16}$", RegexOptions.Compiled);
    public static readonly Regex HashPattern = new("^0x[0-9a-f]{64}$", RegexOptions.Compiled);
    public static readonly Regex WorkerPattern = new("^[0-9a-zA-Z-_]{1,64}$", RegexOptions.Compiled);

    public const ulong ByzantiumHardForkHeight = 4370000;
    public const ulong ConstantinopleHardForkHeight = 7280000;
    public const decimal HomesteadBlockReward = 5.0m;
    public const decimal ByzantiumBlockReward = 3.0m;
    public const decimal ConstantinopleReward = 2.0m;

    public const int MinConfimations = 16;

    public const string RpcRequestWorkerPropertyName = "worker";
}

// Callisto Monetary Policy
// https://github.com/EthereumCommonwealth/Roadmap/issues/56
public class CallistoConstants
{
    public const decimal BaseRewardInitial = 77.76m;
    public const decimal TreasuryPercent = 50m;
}

public enum EthereumNetworkType
{
    Mainnet = 1,
    Ropsten = 3,
    Callisto = 820,

    Unknown = -1,
}

public enum GethChainType
{
    Ethereum,
    Ropsten,
    Callisto,

    Unknown = -1,
}

public static class EthCommands
{
    public const string GetWork = "eth_getWork";
    public const string SubmitWork = "eth_submitWork";
    public const string Sign = "eth_sign";
    public const string GetNetVersion = "net_version";
    public const string GetClientVersion = "web3_clientVersion";
    public const string GetCoinbase = "eth_coinbase";
    public const string GetAccounts = "eth_accounts";
    public const string GetPeerCount = "net_peerCount";
    public const string GetSyncState = "eth_syncing";
    public const string GetBlockNumber = "eth_blockNumber";
    public const string GetBlockByNumber = "eth_getBlockByNumber";
    public const string GetBlockByHash = "eth_getBlockByHash";
    public const string GetUncleByBlockNumberAndIndex = "eth_getUncleByBlockNumberAndIndex";
    public const string GetTxReceipt = "eth_getTransactionReceipt";
    public const string SendTx = "eth_sendTransaction";
    public const string UnlockAccount = "personal_unlockAccount";
    public const string Subscribe = "eth_subscribe";
    public const string MaxPriorityFeePerGas = "eth_maxPriorityFeePerGas";
}
