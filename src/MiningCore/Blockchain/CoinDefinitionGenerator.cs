using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.Monero;
using MiningCore.Blockchain.ZCash;
using MiningCore.Configuration;
using MiningCore.Crypto;
using MiningCore.Crypto.Hashing.Algorithms;
using MiningCore.Crypto.Hashing.Equihash;
using MiningCore.Crypto.Hashing.Special;
using MiningCore.Extensions;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace MiningCore.Blockchain
{
    public static class CoinDefinitionGenerator
    {
        private static readonly Dictionary<CoinType, string> coinNames = new Dictionary<CoinType, string>
        {
            { CoinType.BTC, "Bitcoin" },
            { CoinType.BCH, "Bitcoin Cash" },
            { CoinType.LTC, "Litecoin" },
            { CoinType.DOGE, "Dogecoin," },
            { CoinType.XMR, "Monero" },
            { CoinType.GRS, "GroestlCoin" },
            { CoinType.DGB, "Digibyte" },
            { CoinType.NMC, "Namecoin" },
            { CoinType.VIA, "Viacoin" },
            { CoinType.PPC, "Peercoin" },
            { CoinType.ZEC, "ZCash" },
            { CoinType.ZCL, "ZClassic" },
            { CoinType.ZEN, "Zencash" },
            { CoinType.ETH, "Ethereum" },
            { CoinType.ETC, "Ethereum Classic" },
            { CoinType.EXP, "Expanse" },
            { CoinType.DASH, "Dash" },
            { CoinType.MONA, "Monacoin" },
            { CoinType.VTC, "Vertcoin" },
            { CoinType.BTG, "Bitcoin Gold" },
            { CoinType.GLT, "Globaltoken" },
            { CoinType.ELLA, "Ellaism" },
            { CoinType.AEON, "AEON" },
            { CoinType.STAK, "Straks" },
            { CoinType.ETN, "Electroneum" },
            { CoinType.MOON, "MoonCoin" },
            { CoinType.XVG, "Verge" },
            { CoinType.GBX, "GoByte" },
            { CoinType.CRC, "CrowdCoin" },
            { CoinType.BTCP, "Bitcoin Private" },
            { CoinType.CLO, "Callisto" },
            { CoinType.FLO, "Florincoin" },
            { CoinType.PAK, "PAKcoin" },
            { CoinType.CANN, "CannabisCoin" },
            { CoinType.RVN, "Ravencoin" },
            { CoinType.PGN, "Pigeoncoin" },
            { CoinType.BCD, "Bitcoin Diamond" },
            { CoinType.TUBE, "Bittube" },
        };

        private static readonly Dictionary<Type, double> hashrateMultipliers = new Dictionary<Type, double>
        {
            { typeof(Lyra2Rev2), 4.0 },
            { typeof(X17), 2.55 },
            { typeof(Scrypt), 1.5 },
        };

        private static JsonSerializer serializer;

        public static string Name2Id(string name)
        {
            var id = name.ToLower();
            id = Regex.Replace(id, @"\s", "-");
            id = Regex.Replace(id, @"-+", "-");
            return id;
        }

        public static void WriteCoinDefinitions(string path)
        {
            serializer = new JsonSerializer
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
            };

            var defs = GetCoinDefinitions()
                .ToDictionary(x=> Name2Id(x.Name), x=> x);

            using(var stm = File.OpenWrite(path))
            {
                using(var writer = new StreamWriter(stm, Encoding.UTF8))
                {
                    serializer.Serialize(writer, defs);
                }
            }
        }

        private static CoinDefinition[] GetCoinDefinitions()
        {
            return GetBitcoinDefinitions()
                .Concat(GetEquihashCoinDefinitions())
                .Concat(GetCryptonoteCoinDefinitions())
                .Concat(GetEthereumCoinDefinitions())
                .ToArray();
        }

        private static CoinDefinition[] GetBitcoinDefinitions()
        {
            var result = new[]
            {
                CoinType.BTC, CoinType.BCH, CoinType.NMC, CoinType.PPC,
                CoinType.LTC, CoinType.DOGE, CoinType.VIA, CoinType.DASH,
                CoinType.GRS, CoinType.MONA, CoinType.VTC, CoinType.GLT,
                CoinType.MOON, CoinType.PAK, CoinType.CANN,
                CoinType.RVN, CoinType.PGN, CoinType.BCD, CoinType.FLO
            }
            .Select(x=> GetBitcoinDefinition(x, null, x == CoinType.DASH))
            .ToArray();

            // DGB
            result = result.Concat(new[]
            {
                "Sha256", "Skein", "Qubit", "Groestl"
            }
            .Select(algo => GetBitcoinDefinition(CoinType.DGB, algo)))
            .ToArray();

            // XVG
            result = result.Concat(new[]
            {
                "Lyra", "X17", "Blake", "Groestl"
            }
            .Select(algo => GetBitcoinDefinition(CoinType.XVG, algo)))
            .ToArray();

            return result;
        }

        private static CoinDefinition[] GetEquihashCoinDefinitions()
        {
            var result = new[]
            {
                CoinType.ZEC, CoinType.ZCL, CoinType.ZEN, CoinType.BTG, CoinType.BTCP
            }
            .Select(GetEquihashCoinDefinition)
            .ToArray();

            return result;
        }

        private static CoinDefinition[] GetCryptonoteCoinDefinitions()
        {
            var result = new[]
            {
                CoinType.XMR, CoinType.TUBE
            }
            .Select(GetCryptonoteCoinDefinition)
            .ToArray();

            return result;
        }

        private static CoinDefinition[] GetEthereumCoinDefinitions()
        {
            var result = new[]
            {
                CoinType.ETH, CoinType.ETC
            }
            .Select(GetEthereumCoinDefinition)
            .ToArray();

            return result;
        }

        private static T CreateCoinDefinition<T>(CoinType coin, string algorithm = null) where T: CoinDefinition, new()
        {
            var name = coinNames[coin];

            if (!string.IsNullOrEmpty(algorithm))
                name += $"-{algorithm}";

            var result = new T
            {
                Name = name,
                Symbol = coin.ToString().ToUpper(),
                ExplorerTxLink = CoinMetaData.TxInfoLinks.ContainsKey(coin) ? CoinMetaData.TxInfoLinks[coin] : null,
                ExplorerAccountLink = CoinMetaData.AddressInfoLinks.ContainsKey(coin) ? CoinMetaData.AddressInfoLinks[coin] : null,
            };

            // block info links
            if (CoinMetaData.BlockInfoLinks.ContainsKey(coin))
            {
                if (CoinMetaData.BlockInfoLinks[coin].Count == 1)
                    result.ExplorerBlockLink = CoinMetaData.BlockInfoLinks[coin][string.Empty];
                else
                {
                    result.ExplorerBlockLinks = CoinMetaData.BlockInfoLinks[coin];

                    if (result.ExplorerBlockLinks.ContainsKey(string.Empty))
                    {
                        var value = result.ExplorerBlockLinks[string.Empty];
                        result.ExplorerBlockLinks.Remove(string.Empty);
                        result.ExplorerBlockLinks["block"] = value;
                    }
                }
            }

            return result;
        }

        private static HashInvocation GetHashAlgorithmId(IHashAlgorithm hash)
        {
            if (hash == null)
                return null;

            var result = new HashInvocation();

            if (hash.GetType() != typeof(DigestReverser))
            {
                var name = hash.GetType().Name.ToLower();
                result.Hash = name;

                // special handling for certain algos
                switch(name)
                {
                    case "scrypt":
                        result.Args = new object[] { 1024, 1 };
                        break;

                    case "neoscrypt":
                        result.Args = new object[] { 0x80000620 };
                        break;
                }
            }

            else
            {
                result.Hash = "reverse";

                var reverser = (DigestReverser) hash;
                result.Args = new object[] { GetHashAlgorithmId(reverser.Upstream)};
            }

            return result;
        }

        private static CoinDefinition GetBitcoinDefinition(CoinType coin,
            string algorithm, bool hasMasterNodes = false)
        {
            var result = CreateCoinDefinition<BitcoinDefinition>(coin, algorithm);
            result.Family = CoinFamily.Bitcoin;

            if (coin == CoinType.FLO)
            {
                result.CoinbaseTxVersion = 2u;
                result.CoinbaseTxAppendData = "Flo MiningCore";
            }

            var props = BitcoinProperties.GetCoinProperties(coin, algorithm);
            result.HasMasterNodes = hasMasterNodes;
            result.CoinbaseHasher = JObject.FromObject(GetHashAlgorithmId(props.CoinbaseHasher), serializer);
            result.HeaderHasher = JObject.FromObject(GetHashAlgorithmId(props.HeaderHasher), serializer);
            result.BlockHasher = JObject.FromObject(GetHashAlgorithmId(props.BlockHasher), serializer);

            if(props.PoSBlockHasher != null)
                result.PoSBlockHasher = JObject.FromObject(GetHashAlgorithmId(props.PoSBlockHasher), serializer);

            if (hashrateMultipliers.TryGetValue(props.HeaderHasher.GetType(), out var hashrateMultiplier))
                result.HashrateMultiplier = hashrateMultiplier;
            else
                result.HashrateMultiplier = 1;

            result.ShareMultiplier = props.ShareMultiplier;

            if(coin == CoinType.XVG)
                result.BlockrewardMultiplier = 100;

            return result;
        }


        private static HashInvocation GetEquihashSolverDefinition(Func<EquihashSolverBase> txConfigSolver)
        {
            var solver = txConfigSolver();
            var m = Regex.Match(solver.GetType().Name, @"(\d+)_(\d+)");

            var result = new HashInvocation
            {
                Hash = "equihash",
                Args = new object[] { int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), solver.Personalization }
            };
            return result;
        }

        private static CoinDefinition GetEquihashCoinDefinition(CoinType coin)
        {
            var result = CreateCoinDefinition<EquihashCoinDefinition>(coin);
            result.Family = CoinFamily.Equihash;
            result.UseBitcoinPayoutHandler = coin == CoinType.BTG;

            if (coin == CoinType.BTG)
                result.Subfamily = EquihashSubfamily.BitcoinGold;

            var chain = ZCashConstants.Chains[coin];
            result.Networks = new Dictionary<string, EquihashCoinDefinition.EquihashNetworkDefinition>();
            result.UsesZCashAddressFormat = chain[chain.Keys.First()].UsesZCashAddressFormat;

            foreach (var networkType in chain.Keys)
            {
                var txConfig = chain[networkType];

                var network = new EquihashCoinDefinition.EquihashNetworkDefinition
                {
                    Diff1 = "00" + txConfig.Diff1.ToByteArray().ToHexString(),
                    SolutionSize = txConfig.SolutionSize,
                    SolutionPreambleSize = txConfig.SolutionPreambleSize,
                    Solver = JObject.FromObject(GetEquihashSolverDefinition(txConfig.Solver), serializer),

                    PayFoundersReward = txConfig.PayFoundersReward,
                    FoundersRewardAddresses = txConfig.FoundersRewardAddresses,
                    FoundersRewardSubsidyHalvingInterval = txConfig.FoundersRewardSubsidyHalvingInterval,
                    FoundersRewardSubsidySlowStartInterval = txConfig.FoundersRewardSubsidySlowStartInterval,
                    PercentFoundersReward = txConfig.PercentFoundersReward,

                    TreasuryRewardStartBlockHeight = txConfig.TreasuryRewardStartBlockHeight,
                    TreasuryRewardAddressChangeInterval = txConfig.TreasuryRewardAddressChangeInterval,
                    TreasuryRewardAddresses = txConfig.TreasuryRewardAddresses,
                    PercentTreasuryReward = txConfig.PercentTreasuryReward,

                    OverwinterActivationHeight = txConfig.OverwinterActivationHeight,
                    OverwinterTxVersion = txConfig.OverwinterTxVersion,
                    OverwinterTxVersionGroupId = txConfig.OverwinterTxVersionGroupId,

                    SaplingActivationHeight = txConfig.SaplingActivationHeight,
                    SaplingTxVersion = txConfig.SaplingTxVersion,
                    SaplingTxVersionGroupId = txConfig.SaplingTxVersionGroupId,
                };

                if (coin == CoinType.ZEC)
                {
                    switch(networkType)
                    {
                        case BitcoinNetworkType.Main:
                            network.CoinbaseTxNetwork = ZCashConstants.ZCashNetworkMain.Name;
                            break;
                        case BitcoinNetworkType.Test:
                            network.CoinbaseTxNetwork = ZCashConstants.ZCashNetworkTest.Name;
                            break;
                        case BitcoinNetworkType.RegTest:
                            network.CoinbaseTxNetwork = ZCashConstants.ZCashNetworkReg.Name;
                            break;
                    }
                }

                else
                {
                    switch (networkType)
                    {
                        case BitcoinNetworkType.Main:
                            network.CoinbaseTxNetwork = Network.Main.Name;
                            break;
                        case BitcoinNetworkType.Test:
                            network.CoinbaseTxNetwork = Network.TestNet.Name;
                            break;
                        case BitcoinNetworkType.RegTest:
                            network.CoinbaseTxNetwork = Network.RegTest.Name;
                            break;
                    }
                }

                network.CoinbaseTxNetwork = network.CoinbaseTxNetwork.ToLower();

                result.Networks[networkType.ToString().ToLower()] = network;
            }

            return result;
        }

        private static CoinDefinition GetCryptonoteCoinDefinition(CoinType coin)
        {
            var result = CreateCoinDefinition<CryptonoteCoinDefinition>(coin);
            result.Family = CoinFamily.Cryptonote;

            result.AddressPrefix = MoneroConstants.AddressPrefix[coin];
            result.AddressPrefixTestnet = MoneroConstants.AddressPrefixTestnet[coin];
            result.AddressPrefixIntegrated = MoneroConstants.AddressPrefixIntegrated[coin];
            result.AddressPrefixIntegratedTestnet = MoneroConstants.AddressPrefixIntegratedTestnet[coin];
            result.SmallestUnit = MoneroConstants.SmallestUnit[coin];

            switch (coin)
            {
                case CoinType.XMR:
                    result.Hash = CryptonightHashType.Normal;
                    result.HashVariant = 0; // auto
                    break;

                case CoinType.TUBE:
                    result.Hash = CryptonightHashType.Heavy;
                    result.HashVariant = 2; // Variant TUBE
                    result.BlockrewardMultiplier = 0.7m;
                    break;
            }

            return result;
        }

        private static CoinDefinition GetEthereumCoinDefinition(CoinType coin)
        {
            var result = CreateCoinDefinition<EthereumCoinDefinition>(coin);
            result.Family = CoinFamily.Ethereum;

            return result;
        }
    }
}
