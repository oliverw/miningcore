using System;

namespace MiningCore.Blockchain.Ethereum
{
    public class EthereumUtils
    {
        public static void DetectNetworkAndChain(string netVersionResponse, string parityChainResponse,
            out EthereumNetworkType networkType, out ParityChainType chainType)
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
            if (!Enum.TryParse(parityChainResponse, true, out chainType))
                chainType = ParityChainType.Unknown;
        }
    }
}
