using System;
using System.Linq;
using System.Threading;
using MiningCore.Extensions;

namespace MiningCore.Blockchain.ZCash
{
    public class ZCashExtraNonceProvider : ExtraNonceProviderBase
    {
        public ZCashExtraNonceProvider() : base(3)
        {
        }
    }
}
