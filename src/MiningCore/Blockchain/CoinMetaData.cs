using System;
using System.Collections.Generic;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.Ethereum;
using MiningCore.Configuration;

namespace MiningCore.Blockchain
{
    public static class CoinMetaData
    {
        public static readonly Dictionary<CoinType, Dictionary<string, string>> BlockInfoLinks = new Dictionary<CoinType, Dictionary<string, string>>
        {
            { CoinType.ETH, new Dictionary<string, string>
            {
                { string.Empty, "https://etherscan.io/block/{0}" },
                { EthereumConstants.BlockTypeUncle, "https://etherscan.io/uncle/{0}" },
            }},

            { CoinType.ETC, new Dictionary<string, string>
            {
                { string.Empty, "https://gastracker.io/block/{0}" },
                { EthereumConstants.BlockTypeUncle, "https://gastracker.io/uncle/{0}" }
            }},

            { CoinType.XMR, new Dictionary<string, string> { { string.Empty, "https://chainradar.com/xmr/block/{0}" }}},
            { CoinType.ETN, new Dictionary<string, string> { { string.Empty, "https://blockexplorer.electroneum.com/block/{0}" } }},
            { CoinType.LTC, new Dictionary<string, string> { { string.Empty, "https://chainz.cryptoid.info/ltc/block.dws?{0}.htm" } }},
            { CoinType.BCH, new Dictionary<string, string> { { string.Empty, "https://www.blocktrail.com/BCC/block/{0}" }}},
            { CoinType.DASH, new Dictionary<string, string> { { string.Empty, "https://chainz.cryptoid.info/dash/block.dws?{0}.htm" }}},
            { CoinType.BTC, new Dictionary<string, string> { { string.Empty, "https://blockchain.info/block/{0}" }}},
            { CoinType.DOGE, new Dictionary<string, string> { { string.Empty, "https://dogechain.info/block/{0}" }}},
            { CoinType.ZEC, new Dictionary<string, string> { { string.Empty, "https://explorer.zcha.in/blocks/{0}" }}},
            { CoinType.ZCL, new Dictionary<string, string> { { string.Empty, "http://explorer.zclmine.pro/blocks/{0}" }}},
            { CoinType.ZEN, new Dictionary<string, string> { { string.Empty, "http://explorer.zensystem.io/blocks/{0}" } }},
            { CoinType.DGB, new Dictionary<string, string> { { string.Empty, "https://digiexplorer.info/block/{0}" }}},
            { CoinType.NMC, new Dictionary<string, string> { { string.Empty, "https://explorer.namecoin.info/b/{0}" }}},
            { CoinType.GRS, new Dictionary<string, string> { { string.Empty, "https://groestlsight.groestlcoin.org/block/{0}" }}},
            { CoinType.MONA, new Dictionary<string, string> { { string.Empty, "https://bchain.info/MONA/block/{0}" }}},
            { CoinType.GLT, new Dictionary<string, string> { { string.Empty, "https://bchain.info/GLT/block/{0}" }}},
            { CoinType.VTC, new Dictionary<string, string> { { string.Empty, "https://bchain.info/VTC/block/{0}" }}},
            { CoinType.BTG, new Dictionary<string, string> { { string.Empty, "https://btg-bitcore2.trezor.io/block/{0}" }}},
            { CoinType.ELLA, new Dictionary<string, string> { { string.Empty, "https://explorer.ellaism.org/block/{0}" }}},
            { CoinType.EXP, new Dictionary<string, string> { { string.Empty, "http://www.gander.tech/blocks/{0}" }}},
            { CoinType.AEON, new Dictionary<string, string> { { string.Empty, "https://chainradar.com/aeon/block/{0}" }}},
            { CoinType.STAK, new Dictionary<string, string> { { string.Empty, "https://straks.info/block/{0}" }}},
            { CoinType.MOON, new Dictionary<string, string> { { string.Empty, "https://chainz.cryptoid.info/moon/block.dws?{0}.htm" }}},
            { CoinType.XVG, new Dictionary<string, string> { { string.Empty, "https://verge-blockchain.info/block/{0}" } }},
        };

        public static readonly Dictionary<CoinType, string> TxInfoLinks = new Dictionary<CoinType, string>
        {
            { CoinType.XMR, "https://chainradar.com/xmr/transaction/{0}" },
            { CoinType.ETN, "https://blockexplorer.electroneum.com/tx/{0}" },
            { CoinType.ETH, "https://etherscan.io/tx/{0}" },
            { CoinType.ETC, "https://gastracker.io/tx/{0}" },
            { CoinType.LTC, "https://chainz.cryptoid.info/ltc/tx.dws?{0}.htm" },
            { CoinType.BCH, "https://www.blocktrail.com/BCC/tx/{0}" },
            { CoinType.DASH, "https://chainz.cryptoid.info/dash/tx.dws?{0}.htm" },
            { CoinType.BTC, "https://blockchain.info/tx/{0}" },
            { CoinType.DOGE, "https://dogechain.info/tx/{0}" },
            { CoinType.ZEC, "https://explorer.zcha.in/transactions/{0}" },
            { CoinType.ZCL, "http://explorer.zclmine.pro/transactions/{0}" },
            { CoinType.ZEN, "http://explorer.zensystem.io/transactions/{0}" },
            { CoinType.DGB, "https://digiexplorer.info/tx/{0}" },
            { CoinType.NMC, "https://explorer.namecoin.info/tx/{0}" },
            { CoinType.GRS, "https://groestlsight.groestlcoin.org/tx/{0}" },
            { CoinType.MONA, "https://bchain.info/MONA/tx/{0}" },
            { CoinType.STAK, "https://straks.info/transaction/{0}" },
            { CoinType.GLT, "https://bchain.info/GLT/tx/{0}" },
            { CoinType.VTC, "https://bchain.info/VTC/tx/{0}" },
            { CoinType.BTG, "https://btgexp.com/tx/{0}" },
            { CoinType.ELLA, "https://explorer.ellaism.org/tx/{0}" },
            { CoinType.EXP, "http://www.gander.tech/tx/{0}" },
            { CoinType.AEON, "https://chainradar.com/aeon/transaction/{0}" },
            { CoinType.MOON, "https://chainz.cryptoid.info/moon/tx.dws?{0}.htm" },
            { CoinType.XVG, "https://verge-blockchain.info/tx/{0}" },
            { CoinType.GBX, "http://gobyte.ezmine.io/tx/{0}" },
            { CoinType.CRC, "http://explorer.cryptopros.us/tx/{0}" },
        };
        
        public static readonly Dictionary<CoinType, string> AddressInfoLinks = new Dictionary<CoinType, string>
        {
            { CoinType.ETH, "https://etherscan.io/address/{0}" },
            { CoinType.ETC, "https://gastracker.io/addr/{0}" },
            { CoinType.LTC, "https://chainz.cryptoid.info/ltc/address.dws?{0}.htm" },
            { CoinType.BCH, "https://www.blocktrail.com/BCC/address/{0}" },
            { CoinType.DASH, "https://chainz.cryptoid.info/dash/address.dws?{0}.htm" },
            { CoinType.BTC, "https://blockchain.info/address/{0}" },
            { CoinType.DOGE, "https://dogechain.info/address/{0}" },
            { CoinType.ZEC, "https://explorer.zcha.in/accounts/{0}" },
            { CoinType.ZCL, "http://explorer.zclmine.pro/accounts/{0}" },
            { CoinType.ZEN, "http://explorer.zensystem.io/accounts/{0}" },
            { CoinType.DGB, "https://digiexplorer.info/address/{0}" },
            { CoinType.NMC, "https://explorer.namecoin.info/a/{0}" },
            { CoinType.GRS, "https://groestlsight.groestlcoin.org/address/{0}" },
            { CoinType.MONA, "https://bchain.info/MONA/addr/{0}" },
            { CoinType.STAK, "https://straks.info/address/{0}" },
            { CoinType.GLT, "https://bchain.info/GLT/addr/{0}" },
            { CoinType.VTC, "https://bchain.info/VTC/addr/{0}" },
            { CoinType.BTG, "https://btgexp.com/address/{0}" },
            { CoinType.ELLA, "https://explorer.ellaism.org/addr/{0}" },
            { CoinType.EXP, "http://www.gander.tech/address/{0}" },
            { CoinType.MOON, "https://chainz.cryptoid.info/moon/address.dws?{0}.htm" },
            { CoinType.XVG, "https://verge-blockchain.info/address/{0}" },
            { CoinType.GBX, "http://gobyte.ezmine.io/address/{0}" },
            { CoinType.CRC, "http://explorer.cryptopros.us/address/{0}" },
        };

        private const string Ethash = "Dagger-Hashimoto";
        private const string Cryptonight = "Cryptonight";
        private const string CryptonightLight = "Cryptonight-Light";

        public static readonly Dictionary<CoinType, Func<CoinType, string>> CoinAlgorithm = new Dictionary<CoinType, Func<CoinType, string>>
        {
            { CoinType.ETH, (coin)=> Ethash },
            { CoinType.ETC, (coin)=> Ethash },
            { CoinType.LTC, BitcoinProperties.GetAlgorithm },
            { CoinType.BCH, BitcoinProperties.GetAlgorithm },
            { CoinType.DASH, BitcoinProperties.GetAlgorithm },
            { CoinType.BTC, BitcoinProperties.GetAlgorithm },
            { CoinType.DOGE, BitcoinProperties.GetAlgorithm },
            { CoinType.ZEC, BitcoinProperties.GetAlgorithm },
            { CoinType.ZCL, BitcoinProperties.GetAlgorithm },
            { CoinType.ZEN, BitcoinProperties.GetAlgorithm },
            { CoinType.DGB, BitcoinProperties.GetAlgorithm },
            { CoinType.NMC, BitcoinProperties.GetAlgorithm },
            { CoinType.GRS, BitcoinProperties.GetAlgorithm },
            { CoinType.MONA, BitcoinProperties.GetAlgorithm },
            { CoinType.STAK, BitcoinProperties.GetAlgorithm },
            { CoinType.GLT, BitcoinProperties.GetAlgorithm },
            { CoinType.VTC, BitcoinProperties.GetAlgorithm },
            { CoinType.BTG, BitcoinProperties.GetAlgorithm },
            { CoinType.ELLA, (coin)=> Ethash },
            { CoinType.EXP, (coin)=> Ethash },
            { CoinType.MOON, BitcoinProperties.GetAlgorithm },
            { CoinType.XVG, BitcoinProperties.GetAlgorithm },
            { CoinType.XMR, (coin)=> Cryptonight },
            { CoinType.ETN, (coin)=> Cryptonight },
            { CoinType.AEON, (coin)=> CryptonightLight },
            { CoinType.GBX, BitcoinProperties.GetAlgorithm },
            { CoinType.CRC, BitcoinProperties.GetAlgorithm },
        };
    }
}
