using System;
using System.Collections.Generic;
using MiningForce.Configuration;

namespace MiningForce
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
