using Miningcore.Configuration;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.Configuration
{
    public class BitcoinPoolConfigExtra
    {
        public BitcoinAddressType AddressType { get; set; } = BitcoinAddressType.Legacy;

        /// <summary>
        /// Maximum number of tracked jobs.
        /// Default: 12 - you should increase this value if your blockrefreshinterval is higher than 300ms
        /// </summary>
        public int? MaxActiveJobs { get; set; }

        /// <summary>
        /// Set to true to limit RPC commands to old Bitcoin command set
        /// </summary>
        public bool? HasLegacyDaemon { get; set; }

        /// <summary>
        /// Arbitrary string appended at end of coinbase tx
        /// Overrides property of same name from BitcoinTemplate
        /// </summary>
        public string CoinbaseTxComment { get; set; }

        /// <summary>
        /// Blocktemplate stream published via ZMQ
        /// </summary>
        public ZmqPubSubEndpointConfig BtStream { get; set; }

        /// <summary>
        /// Custom Arguments for getblocktemplate RPC
        /// </summary>
        public JToken GBTArgs { get; set; }
    }
}
