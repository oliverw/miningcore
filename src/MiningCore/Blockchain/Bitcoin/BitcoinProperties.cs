using System;
using System.Collections.Generic;
using System.Text;
using MiningCore.Configuration;
using MiningCore.Crypto;
using MiningCore.Crypto.Hashing.Algorithms;
using MiningCore.Crypto.Hashing.Special;

namespace MiningCore.Blockchain.Bitcoin
{
	public class BitcoinCoinProperties
	{
		public BitcoinCoinProperties(double shareMultiplier,  IHashAlgorithm coinbaseHasher, IHashAlgorithm headerHasher, 
			IHashAlgorithm blockHasher, IHashAlgorithm posblockHasher = null)
		{
			ShareMultiplier = shareMultiplier;
			CoinbaseHasher = coinbaseHasher;
			HeaderHasher = headerHasher;
			BlockHasher = blockHasher;
			PoSBlockHasher = posblockHasher;
		}

		public double ShareMultiplier { get; }
		public IHashAlgorithm CoinbaseHasher { get; }
		public IHashAlgorithm HeaderHasher { get; }
		public IHashAlgorithm BlockHasher { get; }
		public IHashAlgorithm PoSBlockHasher { get; }
	}

	public class BitcoinProperties
	{
		private static readonly IHashAlgorithm sha256s = new Sha256S();
		private static readonly IHashAlgorithm sha256d = new Sha256D();
		private static readonly IHashAlgorithm sha256dReverse = new DigestReverser(sha256d);
		private static readonly IHashAlgorithm x11 = new X11();
		private static readonly IHashAlgorithm groestl = new Groestl();
		private static readonly IHashAlgorithm lyra2Rev2 = new Lyra2Rev2();
		private static readonly IHashAlgorithm scrypt_1024_1 = new Scrypt(1024, 1);

		public static readonly BitcoinCoinProperties Sha256Coin = 
			new BitcoinCoinProperties(1, sha256d, sha256d, sha256dReverse);

		public static readonly BitcoinCoinProperties ScryptCoin = 
			new BitcoinCoinProperties(Math.Pow(2, 16), sha256d, scrypt_1024_1, sha256dReverse, new DigestReverser(scrypt_1024_1));

		public static readonly BitcoinCoinProperties GroestlCoin =
			new BitcoinCoinProperties(Math.Pow(2, 8), sha256s, groestl, new DigestReverser(groestl));

		public static readonly BitcoinCoinProperties Lyra2Rev2Coin =
			new BitcoinCoinProperties(Math.Pow(2, 8), sha256d, lyra2Rev2, sha256dReverse);

		public static readonly BitcoinCoinProperties X11Coin =
			new BitcoinCoinProperties(1, sha256d, x11, new DigestReverser(x11));

		public static readonly Dictionary<CoinType, BitcoinCoinProperties> CoinProperties = new Dictionary<CoinType, BitcoinCoinProperties>
		{
			
		};
    }
}
