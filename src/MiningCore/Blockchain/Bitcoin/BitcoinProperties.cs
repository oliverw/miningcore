using System;
using System.Collections.Generic;
using MiningCore.Configuration;
using MiningCore.Contracts;
using MiningCore.Crypto;
using MiningCore.Crypto.Hashing.Algorithms;
using MiningCore.Crypto.Hashing.Special;

namespace MiningCore.Blockchain.Bitcoin
{
    public class BitcoinCoinProperties
    {
        public BitcoinCoinProperties(double shareMultiplier, IHashAlgorithm coinbaseHasher, IHashAlgorithm headerHasher,
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
        private static readonly IHashAlgorithm sha256S = new Sha256S();
        private static readonly IHashAlgorithm sha256D = new Sha256D();
        private static readonly IHashAlgorithm sha256DReverse = new DigestReverser(sha256D);
        private static readonly IHashAlgorithm x11 = new X11();
        private static readonly IHashAlgorithm groestl = new Groestl();
        private static readonly IHashAlgorithm lyra2Rev2 = new Lyra2Rev2();
        private static readonly IHashAlgorithm scrypt_1024_1 = new Scrypt(1024, 1);
        private static readonly IHashAlgorithm skein = new Skein();
        private static readonly IHashAlgorithm qubit = new Qubit();
        private static readonly IHashAlgorithm groestlMyriad = new GroestlMyriad();

        private static readonly BitcoinCoinProperties sha256Coin =
            new BitcoinCoinProperties(1, sha256D, sha256D, sha256DReverse);

        private static readonly BitcoinCoinProperties scryptCoin =
            new BitcoinCoinProperties(Math.Pow(2, 16), sha256D, scrypt_1024_1, sha256DReverse, new DigestReverser(scrypt_1024_1));

        private static readonly BitcoinCoinProperties groestlCoin =
            new BitcoinCoinProperties(Math.Pow(2, 8), sha256S, groestl, new DigestReverser(groestl));

        private static readonly BitcoinCoinProperties lyra2Rev2CoinVariantA =
            new BitcoinCoinProperties(Math.Pow(2, 8), sha256D, lyra2Rev2, sha256DReverse);

        private static readonly BitcoinCoinProperties lyra2Rev2CoinVariantB =
            new BitcoinCoinProperties(Math.Pow(2, 8), sha256D, lyra2Rev2, new DigestReverser(lyra2Rev2));

        private static readonly BitcoinCoinProperties x11Coin =
            new BitcoinCoinProperties(1, sha256D, x11, new DigestReverser(x11));

        private static readonly BitcoinCoinProperties skeinCoin =
            new BitcoinCoinProperties(1, sha256D, skein, sha256DReverse);

        private static readonly BitcoinCoinProperties qubitCoin =
            new BitcoinCoinProperties(1, sha256D, qubit, sha256DReverse);

        private static readonly BitcoinCoinProperties groestlMyriadCoin =
            new BitcoinCoinProperties(Math.Pow(2, 8), sha256S, groestlMyriad, sha256DReverse);

        private static readonly BitcoinCoinProperties equihashCoin =
            new BitcoinCoinProperties(1, new DummyHasher(), sha256D, sha256DReverse);

        private static readonly Dictionary<CoinType, BitcoinCoinProperties> coinProperties = new Dictionary<CoinType, BitcoinCoinProperties>
        {
            // SHA256
            { CoinType.BTC, sha256Coin },
            { CoinType.BCC, sha256Coin },
            { CoinType.NMC, sha256Coin },
            { CoinType.PPC, sha256Coin },
			{ CoinType.GLT, sha256Coin },

            // Scrypt
            { CoinType.LTC, scryptCoin },
            { CoinType.DOGE, scryptCoin },
            { CoinType.VIA, scryptCoin },

            // Groestl
            { CoinType.GRS, groestlCoin },

            // Lyra2Rev2 - Variant A
            { CoinType.MONA, lyra2Rev2CoinVariantA },
            { CoinType.VTC, lyra2Rev2CoinVariantA },

            // Lyra2Rev2 - Variant B
            { CoinType.STAK, lyra2Rev2CoinVariantB },

            // X11
            { CoinType.DASH, x11Coin },

            // Equihash
            { CoinType.ZEC, equihashCoin },
            { CoinType.BTG, equihashCoin },
        };

        public static BitcoinCoinProperties GetCoinProperties(CoinType coin, string algorithm = null)
        {
            if (coin == CoinType.DGB)
                return GetDigiByteProperties(algorithm);

            coinProperties.TryGetValue(coin, out var props);
            return props;
        }

        private static BitcoinCoinProperties GetDigiByteProperties(string algorithm)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(algorithm), $"{nameof(algorithm)} must not be empty");

            switch(algorithm)
            {
                case "sha256d":
                    return sha256Coin;

                case "skein":
                    return skeinCoin;

                case "qubit":
                    return qubitCoin;

                case "groestl":
                case "groestl-myriad":
                    return groestlMyriadCoin;

                default: // scrypt
                    return scryptCoin;
            }
        }
    }
}
