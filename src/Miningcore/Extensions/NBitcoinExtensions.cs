using System;
using Miningcore.Blockchain.Bitcoin;
using NBitcoin;

namespace Miningcore.Extensions
{
    public static class MiscExtensions
    {
        public static Network ToNetwork(this BitcoinNetworkType networkType)
        {
            switch (networkType)
            {
                case BitcoinNetworkType.Main:
                    return Network.Main;

                case BitcoinNetworkType.Test:
                    return Network.TestNet;

                case BitcoinNetworkType.RegTest:
                    return Network.RegTest;

                default:
                    throw new NotSupportedException("unsupported network type");
            }
        }
    }
}
