using System.Collections.Generic;
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
            { CoinType.LTC, new Dictionary<string, string> { { string.Empty, "http://explorer.litecoin.net/block/{0}" }}},
            { CoinType.BCH, new Dictionary<string, string> { { string.Empty, "https://www.blocktrail.com/BCC/block/{0}" }}},
            { CoinType.DASH, new Dictionary<string, string> { { string.Empty, "https://chainz.cryptoid.info/dash/block.dws?{0}.htm" }}},
            { CoinType.BTC, new Dictionary<string, string> { { string.Empty, "https://blockchain.info/block/{0}" }}},
            { CoinType.DOGE, new Dictionary<string, string> { { string.Empty, "https://dogechain.info/block/{0}" }}},
            { CoinType.ZEC, new Dictionary<string, string> { { string.Empty, "https://explorer.zcha.in/blocks/{0}" }}},
            { CoinType.DGB, new Dictionary<string, string> { { string.Empty, "https://digiexplorer.info/block/{0}" }}},
            { CoinType.NMC, new Dictionary<string, string> { { string.Empty, "https://explorer.namecoin.info/b/{0}" }}},
            { CoinType.GRS, new Dictionary<string, string> { { string.Empty, "https://groestlsight.groestlcoin.org/block/{0}" }}},
            { CoinType.MONA, new Dictionary<string, string> { { string.Empty, "https://bchain.info/MONA/block/{0}" }}},
            { CoinType.GLT, new Dictionary<string, string> { { string.Empty, "https://bchain.info/GLT/block/{0}" }}},
            { CoinType.VTC, new Dictionary<string, string> { { string.Empty, "https://bchain.info/VTC/block/{0}" }}},
            //{ CoinType.BTG, new Dictionary<string, string> { { string.Empty, "https://btgexp.com/block/{0}" }}},
            { CoinType.ELLA, new Dictionary<string, string> { { string.Empty, "https://explorer.ellaism.org/block/{0}" }}},
            { CoinType.EXP, new Dictionary<string, string> { { string.Empty, "http://www.gander.tech/blocks/{0}" }}},
            { CoinType.AEON, new Dictionary<string, string> { { string.Empty, "https://chainradar.com/aeon/block/{0}" }}},
            { CoinType.STAK, new Dictionary<string, string> { { string.Empty, "https://straks.info/block/{0}" }}},
        };

        public static readonly Dictionary<CoinType, string> PaymentInfoLinks = new Dictionary<CoinType, string>
        {
            { CoinType.XMR, "https://chainradar.com/xmr/transaction/{0}" },
            { CoinType.ETN, "https://blockexplorer.electroneum.com/tx/{0}" },
            { CoinType.ETH, "https://etherscan.io/tx/{0}" },
            { CoinType.ETC, "https://gastracker.io/tx/{0}" },
            { CoinType.LTC, "http://explorer.litecoin.net/tx/{0}" },
            { CoinType.BCH, "https://www.blocktrail.com/BCC/tx/{0}" },
            { CoinType.DASH, "https://chainz.cryptoid.info/dash/tx.dws?{0}.htm" },
            { CoinType.BTC, "https://blockchain.info/tx/{0}" },
            { CoinType.DOGE, "https://dogechain.info/tx/{0}" },
            { CoinType.ZEC, "https://explorer.zcha.in/transactions/{0}" },
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
        };

        public static readonly Dictionary<CoinType, string> AddressInfoLinks = new Dictionary<CoinType, string>
        {
            { CoinType.ETH, "https://etherscan.io/address/{0}" },
            { CoinType.ETC, "https://gastracker.io/addr/{0}" },
            { CoinType.LTC, "http://explorer.litecoin.net/address/{0}" },
            { CoinType.BCH, "https://www.blocktrail.com/BCC/address/{0}" },
            { CoinType.DASH, "https://chainz.cryptoid.info/dash/address.dws?{0}.htm" },
            { CoinType.BTC, "https://blockchain.info/address/{0}" },
            { CoinType.DOGE, "https://dogechain.info/address/{0}" },
            { CoinType.ZEC, "https://explorer.zcha.in/accounts/{0}" },
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
        };
    }
}
