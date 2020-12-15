using System;
using System.Linq;
using System.Threading;
using Miningcore.Extensions;

namespace Miningcore.Blockchain.Equihash
{
    public class EquihashExtraNonceProvider : ExtraNonceProviderBase
    {
        public EquihashExtraNonceProvider() : base(4)
        {
        }
    }
}
