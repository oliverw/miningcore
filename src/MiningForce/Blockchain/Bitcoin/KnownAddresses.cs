using System.Collections.Generic;
using MiningForce.Configuration;

namespace MiningForce.Blockchain.Bitcoin
{
    public class KnownAddresses
    {
	    public static readonly Dictionary<CoinType, string> DevFeeAddresses = new Dictionary<CoinType, string>
	    {
		    {CoinType.BTC, "17QnVor1B6oK1rWnVVBrdX9gFzVkZZbhDm"},
		    {CoinType.LTC, "LTK6CWastkmBzGxgQhTTtCUjkjDA14kxzC"},
		    {CoinType.DOGE, "DGDuKRhBewGP1kbUz4hszNd2p6dDzWYy9Q"},
		    {CoinType.NMC, "NDSLDpFEcTbuRVcWHdJyiRZThVAcb5Z79o"},
		    {CoinType.DGB, "DAFtYMGVdNtqHJoBGg2xqZZwSuYAaEs2Bn"},
		    {CoinType.PPC, "PE8RH6HAvi8sqYg47D58TeKTjyeQFFHWR2"},
		    {CoinType.VIA, "Vc5rJr2QdA2yo1jBoqYUAH7T59uBh2Vw5q"},
	    };
    }
}
