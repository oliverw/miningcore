/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using Miningcore.Configuration;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.Configuration
{
    public class BitcoinPoolConfigExtra
    {
        public BitcoinAddressType AddressType { get; set; } = BitcoinAddressType.Legacy;

        /// <summary>
        /// Set bech32 prefix if it is not the standard bech prefix of btc
        /// </summary>
        public string BechPrefix {get;set;} = "bc";

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
