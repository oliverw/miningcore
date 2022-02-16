using System.Globalization;
using System.Numerics;

namespace Miningcore.Blockchain.Ethereum;

public static class EthereumUtils
{
    public static void DetectNetworkAndChain(string netVersionResponse, string gethChainResponse, string chainIdResponse,
        out EthereumNetworkType networkType, out GethChainType chainType, out BigInteger chainId)
    {
        // convert network
        if(int.TryParse(netVersionResponse, out var netWorkTypeInt))
        {
            networkType = (EthereumNetworkType) netWorkTypeInt;

            if(!Enum.IsDefined(typeof(EthereumNetworkType), networkType))
                networkType = EthereumNetworkType.Unknown;
        }

        else
            networkType = EthereumNetworkType.Unknown;

        // convert chain
        if(!Enum.TryParse(gethChainResponse, true, out chainType))
        {
            chainType = GethChainType.Unknown;
        }

        if(gethChainResponse.ToLower() == "ethereum classic")
        {
            chainType = GethChainType.Ethereum;
        }

        // convert chainId
        if(chainIdResponse.ToLower().StartsWith("0x")) 
        {
            chainIdResponse = chainIdResponse.Replace("0x", "0", StringComparison.OrdinalIgnoreCase);
        }

        if(!BigInteger.TryParse(chainIdResponse, NumberStyles.AllowHexSpecifier, null, out chainId))
        {
            chainId = 0;
        }
    }
}
