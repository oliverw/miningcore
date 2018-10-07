using System;
using System.Globalization;
using System.Numerics;
using Autofac;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Crypto;
using Miningcore.Crypto.Hashing.Algorithms;
using NBitcoin;
using Newtonsoft.Json;

namespace Miningcore.Configuration
{
    public abstract partial class CoinTemplate
    {
        public T As<T>() where T : CoinTemplate
        {
            return (T) this;
        }

        public abstract string GetAlgorithmName(IComponentContext ctx);

        /// <summary>
        /// json source file where this template originated from
        /// </summary>
        [JsonIgnore]
        public string Source { get; set; }
    }

    public partial class BitcoinTemplate
    {
        #region Overrides of CoinDefinition

        public override string GetAlgorithmName(IComponentContext ctx)
        {
            var hash = HashAlgorithmFactory.GetHash(ctx, HeaderHasher);

            if (hash.GetType() == typeof(DigestReverser))
                return ((DigestReverser)hash).Upstream.GetType().Name.ToLower();

            return hash.GetType().Name.ToLower();
        }

        #endregion
    }

    public partial class EquihashCoinTemplate
    {
        public partial class EquihashNetworkDefinition
        {
            public EquihashNetworkDefinition()
            {
                diff1Value = new Lazy<NBitcoin.BouncyCastle.Math.BigInteger>(() =>
                {
                    if(string.IsNullOrEmpty(Diff1))
                        throw new InvalidOperationException("Diff1 has not yet been initialized");

                    return new NBitcoin.BouncyCastle.Math.BigInteger(Diff1, 16);
                });

                diff1BValue = new Lazy<BigInteger>(() =>
                {
                    if (string.IsNullOrEmpty(Diff1))
                        throw new InvalidOperationException("Diff1 has not yet been initialized");

                    return BigInteger.Parse(Diff1, NumberStyles.HexNumber);
                });
            }

            private readonly Lazy<NBitcoin.BouncyCastle.Math.BigInteger> diff1Value;
            private readonly Lazy<BigInteger> diff1BValue;

            [JsonIgnore]
            public NBitcoin.BouncyCastle.Math.BigInteger Diff1Value => diff1Value.Value;

            [JsonIgnore]
            public BigInteger Diff1BValue => diff1BValue.Value;

            [JsonIgnore]
            public ulong FoundersRewardSubsidySlowStartShift => FoundersRewardSubsidySlowStartInterval / 2;

            [JsonIgnore]
            public ulong LastFoundersRewardBlockHeight => FoundersRewardSubsidyHalvingInterval + FoundersRewardSubsidySlowStartShift - 1;
        }

        public EquihashNetworkDefinition GetNetwork(BitcoinNetworkType networkType)
        {
            switch(networkType)
            {
                case BitcoinNetworkType.Main:
                    return Networks["main"];

                case BitcoinNetworkType.Test:
                    return Networks["test"];

                case BitcoinNetworkType.RegTest:
                    return Networks["regtest"];
            }

            throw new NotSupportedException();
        }

        #region Overrides of CoinDefinition

        public override string GetAlgorithmName(IComponentContext ctx)
        {
            // TODO: return variant
            return "Equihash";
        }

        #endregion
    }

    public partial class CryptonoteCoinTemplate
    {
        #region Overrides of CoinDefinition

        public override string GetAlgorithmName(IComponentContext ctx)
        {
            switch (Hash)
            {
                case CryptonightHashType.Normal:
                    return "Cryptonight";
                case CryptonightHashType.Lite:
                    return "Cryptonight-Lite";
                case CryptonightHashType.Heavy:
                    return "Cryptonight-Heavy";
            }

            throw new NotSupportedException("Invalid hash type");
        }

        #endregion
    }

    public partial class EthereumCoinTemplate
    {
        #region Overrides of CoinDefinition

        public override string GetAlgorithmName(IComponentContext ctx)
        {
            return "Ethhash";
        }

        #endregion
    }

    public partial class PoolConfig
    {
        /// <summary>
        /// Back-reference to coin template for this pool
        /// </summary>
        [JsonIgnore]
        public CoinTemplate Template { get; set; }
    }
}
