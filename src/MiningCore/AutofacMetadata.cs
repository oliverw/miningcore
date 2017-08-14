using System;
using System.Collections.Generic;
using MiningCore.Configuration;

namespace MiningCore
{
    public class CoinMetadataAttribute : Attribute
    {
        public CoinMetadataAttribute(IDictionary<string, object> values)
        {
            SupportedCoins = (CoinType[]) values[nameof(SupportedCoins)];
        }

        public CoinMetadataAttribute(params CoinType[] supportedCoins)
        {
            SupportedCoins = supportedCoins;
        }

        public CoinType[] SupportedCoins { get; }
    }
}
