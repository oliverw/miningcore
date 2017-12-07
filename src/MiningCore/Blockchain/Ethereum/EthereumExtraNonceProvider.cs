using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using MiningCore.Extensions;

namespace MiningCore.Blockchain.Ethereum
{
    public class EthereumExtraNonceProvider : ExtraNonceProviderBase
    {
        public EthereumExtraNonceProvider() : base(2)
        {
        }
    }
}
