using System.Collections.Generic;
using MiningCore.Configuration;

namespace MiningCore.Blockchain
{
    public static class CoinMetaData
    {
        public static readonly Dictionary<CoinType, string> BlockInfoLinks = new Dictionary<CoinType, string>
        {
            { CoinType.XMR,  "https://chainradar.com/xmr/block/{0}" },
            { CoinType.ETH,  "https://etherscan.io/block/{0}" },
            { CoinType.ETC,  "https://gastracker.io/block/{0}" },
            { CoinType.LTC,  "http://explorer.litecoin.net/block/{0}" },
            { CoinType.BCC,  "https://www.blocktrail.com/BCC/block/{0}" },
            { CoinType.DASH, "https://chainz.cryptoid.info/dash/block.dws?{0}.htm" },
            { CoinType.BTC,  "https://blockchain.info/block/{0}" },
            { CoinType.DOGE, "https://dogechain.info/block/{0}" },
            { CoinType.ZEC,  "https://explorer.zcha.in/blocks/{0}" },
            { CoinType.DGB,  "https://digiexplorer.info/block/{0}" },
            { CoinType.NMC,  "https://explorer.namecoin.info/b/{0}" },
            { CoinType.GRS,  "https://bchain.info/GRS/block/{0}" },
        };

        public static readonly Dictionary<CoinType, string> PaymentInfoLinks = new Dictionary<CoinType, string>
        {
            { CoinType.XMR,  "https://chainradar.com/xmr/transaction/{0}" },
            { CoinType.ETH,  "https://etherscan.io/tx/{0}" },
            { CoinType.ETC,  "https://gastracker.io/tx/{0}" },
            { CoinType.LTC,  "http://explorer.litecoin.net/tx/{0}" },
            { CoinType.BCC,  "https://www.blocktrail.com/BCC/tx/{0}" },
            { CoinType.DASH, "https://chainz.cryptoid.info/dash/tx.dws?{0}.htm" },
            { CoinType.BTC,  "https://blockchain.info/tx/{0}" },
            { CoinType.DOGE, "https://dogechain.info/tx/{0}" },
            { CoinType.ZEC,  "https://explorer.zcha.in/transactions/{0}" },
            { CoinType.DGB,  "https://digiexplorer.info/tx/{0}" },
            { CoinType.NMC,  "https://explorer.namecoin.info/tx/{0}" },
            { CoinType.GRS,  "https://bchain.info/GRS/tx/{0}" },
        };
    }
}
