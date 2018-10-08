using System;
using System.Collections.Generic;
using System.Text;
using Miningcore.Blockchain.Bitcoin;
using NBitcoin;

namespace Miningcore.Extensions
{
    public static class NBitcoinExtensions
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
