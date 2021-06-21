using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Miningcore.Extensions;

namespace Miningcore.Blockchain.Ethereum
{
    public class EthereumExtraNonceProvider : ExtraNonceProviderBase
    {
        public EthereumExtraNonceProvider(byte? clusterInstanceId) : base(2, clusterInstanceId)
        {
        }
    }
}
