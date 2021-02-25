using System;
using System.Linq;
using System.Numerics;

namespace Miningcore.Blockchain.Ethereum
{
    public class EthereumUtils
    {
        public static void DetectNetworkAndChain(string netVersionResponse, string parityChainResponse,
            out EthereumNetworkType networkType, out ParityChainType chainType)
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
            if(!Enum.TryParse(parityChainResponse, true, out chainType))
            {
                if(parityChainResponse.ToLower() == "ethereum classic")
                    chainType = ParityChainType.Classic;
                else
                    chainType = ParityChainType.Unknown;
            }

            if(chainType == ParityChainType.Foundation)
                chainType = ParityChainType.Mainnet;

            if(chainType == ParityChainType.Joys)
                chainType = ParityChainType.Joys;
        }
        
        public static string GetTargetHex(BigInteger difficulty)
        {
            var target = BigInteger.Divide(BigInteger.Pow(2, 256), difficulty);
            var hex = target.ToString("X16").ToLower();
            return $"0x{string.Concat(Enumerable.Repeat("0", 64 - hex.Length))}{hex}";
        }
    }
}
