using System;
using System.Collections.Generic;
using MiningForce.Configuration;

namespace MiningForce
{
	public class SupportedCoinsMetadataAttribute : Attribute
	{
		public SupportedCoinsMetadataAttribute(IDictionary<string, object> values)
		{
			SupportedCoins = (CoinType[]) values[nameof(SupportedCoins)];
		}

		public SupportedCoinsMetadataAttribute(params CoinType[] supportedCoins)
		{
			SupportedCoins = supportedCoins;
		}

		public CoinType[] SupportedCoins { get; }
	}
}
