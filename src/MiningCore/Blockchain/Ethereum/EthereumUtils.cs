using System;

namespace MiningCore.Blockchain.Ethereum
{
    public class EthereumUtils
    {
        public static void DetectNetworkAndChain(string netVersionResponse, string parityChainResponse,
            out EthereumNetworkType networkType, out EthereumChainType chainType)
        {
            // convert network
            int netWorkTypeInt = 0;
            if (int.TryParse(netVersionResponse, out netWorkTypeInt))
            {
                networkType = (EthereumNetworkType)netWorkTypeInt;

                if (!Enum.IsDefined(typeof(EthereumNetworkType), networkType))
                    networkType = EthereumNetworkType.Unknown;
            }

            else
                networkType = EthereumNetworkType.Unknown;

            // convert chain
            int chainTypeInt = 0;
            if (int.TryParse(parityChainResponse, out chainTypeInt))
            {
                chainType = (EthereumChainType)chainTypeInt;

                if (!Enum.IsDefined(typeof(EthereumChainType), chainType))
                    chainType = EthereumChainType.Unknown;
            }

            else
                chainType = EthereumChainType.Unknown;
        }
    }
}
