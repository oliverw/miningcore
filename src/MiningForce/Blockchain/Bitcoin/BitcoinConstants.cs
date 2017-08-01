using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using MiningForce.Configuration;

namespace MiningForce.Blockchain.Bitcoin
{
	public enum BitcoinNetworkType
	{
		Main = 1,
		Test,
		RegTest
	}

	public enum BitcoinTransactionCategory
	{
		/// <summary>
		/// wallet sending payment
		/// </summary>
		Send = 1,

		/// <summary>
		/// wallet receiving payment in a regular transaction
		/// </summary>
		Receive,

		/// <summary>
		/// matured and spendable coinbase transaction 
		/// </summary>
		Generate,

		/// <summary>
		/// coinbase transaction that is not spendable yet
		/// </summary>
		Immature,

		/// <summary>
		/// coinbase transaction from a block that’s not in the local best block chain
		/// </summary>
		Orphan
	}

	public class BitcoinConstants
	{
		public const decimal SatoshisPerBitcoin = 100000000;
	}

	public class BitcoinCoinsMetaData : CoinMetadataAttribute
	{
		public BitcoinCoinsMetaData() : base(
			CoinType.BTC,
			CoinType.NMC,
			CoinType.PPC,
			CoinType.LTC,
			CoinType.DOGE,
			CoinType.EMC2,
			CoinType.DGB,
			CoinType.VIA,
			CoinType.GRS)
		{
		}
	}
}
