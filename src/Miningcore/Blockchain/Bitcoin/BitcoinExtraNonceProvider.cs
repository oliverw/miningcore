using System;
using System.Security.Cryptography;
using System.Threading;

namespace Miningcore.Blockchain.Bitcoin
{
    public class BitcoinExtraNonceProvider : ExtraNonceProviderBase
    {
        public BitcoinExtraNonceProvider(byte? clusterInstanceId) : base(4, clusterInstanceId)
        {
        }
    }
}
